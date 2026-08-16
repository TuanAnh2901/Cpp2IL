using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Removes the IL2CPP runtime-metadata initialization guards the compiler emits near the top of
/// (almost) every method.
/// </summary>
public static class MetadataInitGuardRemover
{
    private const string InitializeRuntimeMetadata = "il2cpp_codegen_initialize_runtime_metadata";
    private const string InitializeMethod = "il2cpp_codegen_initialize_method";
    private const string RuntimeClassInitExport = "il2cpp_runtime_class_init_export";
    private const string RuntimeClassInitActual = "il2cpp_runtime_class_init_actual";

    public static void Run(MethodAnalysisContext method) => Run(method.ControlFlowGraph!);

    public static void Run(ISILControlFlowGraph cfg)
    {
        var removedAny = false;

        foreach (var guard in cfg.Blocks.ToList())
            removedAny |= TryRemoveGuard(cfg, guard);

        if (removedAny)
            DeadCodeEliminator.Run(cfg);
    }

    private static bool TryRemoveGuard(ISILControlFlowGraph cfg, Block guard)
    {
        if (guard.BlockType != BlockType.TwoWay || guard.Successors.Count != 2
            || guard.Instructions.Count == 0 || guard.Instructions[^1].OpCode != OpCode.ConditionalJump)
            return false;

        // Either successor could be the init entry; the other is then the merge.
        var first = guard.Successors[0];
        var second = guard.Successors[1];

        return TryExcise(cfg, guard, first, second) || TryExcise(cfg, guard, second, first);
    }

    private static bool TryExcise(ISILControlFlowGraph cfg, Block guard, Block initEntry, Block merge)
    {
        if (merge == cfg.EntryBlock || merge == cfg.ExitBlock)
            return false;

        if (!TryCollectRegion(cfg, guard, initEntry, merge, out var region))
            return false;

        Excise(cfg, guard, initEntry, merge, region);
        return true;
    }

    private static bool TryCollectRegion(ISILControlFlowGraph cfg, Block guard, Block initEntry, Block merge,
        out HashSet<Block> region)
    {
        region = [];

        if (initEntry == merge || initEntry == guard)
            return false;

        var sawInitCall = false;
        var sawFlagStore = false;
        var reconverges = false;
        var sawClassInit = false;

        var queue = new Queue<Block>();
        queue.Enqueue(initEntry);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            if (block == merge)
            {
                reconverges = true;
                continue;
            }

            // The region must not run into the method boundary or loop back through the guard.
            if (block == cfg.EntryBlock || block == cfg.ExitBlock || block == guard)
                return false;

            if (!region.Add(block))
                continue;

            if (!ClassifyBlock(block, ref sawInitCall, ref sawFlagStore, ref sawClassInit))
                return false;

            foreach (var successor in block.Successors)
                queue.Enqueue(successor);
        }

        // Method-init guards (il2cpp_codegen_initialize_*) store a flag after running; class-init
        // guards (il2cpp_runtime_class_init) have no flag - the check is on the class pointer itself.
        if (!sawInitCall || !reconverges)
            return false;

        if (!sawClassInit && !sawFlagStore)
            return false;

        var collected = region;
        foreach (var block in collected)
        {
            if (block.Predecessors.Any(predecessor => predecessor != guard && !collected.Contains(predecessor)))
                return false;
            if (block.Successors.Any(successor => successor != merge && !collected.Contains(successor)))
                return false;
        }

        return true;
    }

    // A region block is acceptable only if every instruction is intra-region control flow, an init
    // call, the flag store, or otherwise side-effect-free (writes a local, not memory). A managed call
    // or any other store would have an effect we cannot silently drop, so it disqualifies the region.
    private static bool ClassifyBlock(Block block, ref bool sawInitCall, ref bool sawFlagStore, ref bool sawClassInit)
    {
        foreach (var instruction in block.Instructions)
        {
            switch (instruction.OpCode)
            {
                case OpCode.Jump:
                    break;

                case OpCode.Call or OpCode.CallVoid:
                    if (instruction.Operands is not [string name, ..] || name is not (InitializeRuntimeMetadata or InitializeMethod or RuntimeClassInitExport or RuntimeClassInitActual))
                        return false;
                    sawInitCall = true;
                    if (name is RuntimeClassInitExport or RuntimeClassInitActual)
                        sawClassInit = true;
                    break;

                case OpCode.Move when instruction.Operands is [MemoryOperand { IsConstant: true }, _]:
                    // Absolute-address flag store (method-init guard).
                    sawFlagStore = true;
                    break;

                case OpCode.Move when instruction.Operands is [MemoryOperand { Base: LocalVariable }, var source]
                    && Instruction.IsConstantValue(source):
                    // Class-init flag store: `Move [classPtr+off], 1` writes the runtime's
                    // initialized flag through the class pointer local. It is only acceptable
                    // inside a guard region (which also has the init call), so it is safe to drop.
                    sawFlagStore = true;
                    break;

                default:
                    if (!IsSideEffectFree(instruction))
                        return false;
                    break;
            }
        }

        return true;
    }

    // True for instructions that only compute a value into a local (or do nothing). A store - any
    // instruction whose destination operand is a memory or field reference rather than a local - is
    // excluded, as is anything that transfers control out of the region (return) or merges values
    // from outside (phi with external inputs).
    private static bool IsSideEffectFree(Instruction instruction) =>
        instruction.OpCode switch
        {
            OpCode.Nop => true,
            OpCode.Phi => true,
            // Intra-region branches (jump between region blocks) carry no effect of their own.
            OpCode.Jump or OpCode.ConditionalJump or OpCode.IndirectJump => true,
            OpCode.Move or OpCode.Add or OpCode.Subtract or OpCode.Multiply or OpCode.Divide
                or OpCode.ShiftLeft or OpCode.ShiftRight or OpCode.And or OpCode.Or or OpCode.Xor
                or OpCode.Not or OpCode.Negate
                or (>= OpCode.CheckEqual and <= OpCode.CheckLessOrEqual)
                => instruction.Operands is [LocalVariable, ..],
            _ => false,
        };

    private static void Excise(ISILControlFlowGraph cfg, Block guard, Block initEntry, Block merge, HashSet<Block> region)
    {
        // 1. Repair the merge's phis: drop the inputs from the region's back-edges.
        for (var i = merge.Predecessors.Count - 1; i >= 0; i--)
        {
            if (!region.Contains(merge.Predecessors[i]))
                continue;

            foreach (var phi in merge.Instructions)
                if (phi.OpCode == OpCode.Phi && 1 + i < phi.Operands.Count)
                    phi.Operands.RemoveAt(1 + i);

            merge.Predecessors.RemoveAt(i);
        }

        // 2. Fold the guard so it goes straight to the merge.
        guard.Successors.Remove(initEntry);
        initEntry.Predecessors.Remove(guard);

        var terminator = guard.Instructions[^1];
        terminator.OpCode = OpCode.Jump;
        terminator.Operands = [merge];
        guard.CalculateBlockType();

        // 3. Delete the region. 
        foreach (var block in region)
        {
            foreach (var successor in block.Successors)
                successor.Predecessors.Remove(block);
            foreach (var predecessor in block.Predecessors)
                predecessor.Successors.Remove(block);

            block.Successors.Clear();
            block.Predecessors.Clear();
            cfg.Blocks.Remove(block);
        }
    }
}
