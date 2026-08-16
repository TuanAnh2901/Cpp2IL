using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils.AsmResolver;

namespace Cpp2IL.Core;

public static class IlGenerator
{
    public static void GenerateIl(MethodAnalysisContext context, MethodDefinition definition)
    {
        var assembly = context.DeclaringType!.DeclaringAssembly;
        var module = definition.DeclaringModule!;
        var importer = module.DefaultImporter;
        var factory = module.CorLibTypeFactory;        var writeLine = factory.CorLibScope
            .CreateTypeReference("System", "Console")
            .CreateMemberReference("WriteLine", MethodSignature.CreateStatic(factory.Void, [factory.String]))
            .ImportWith(importer);

        var stringType = factory.CorLibScope.CreateTypeReference("System", "String");
        var stringCtor = stringType
            .CreateMemberReference(".ctor", MethodSignature.CreateStatic(stringType.ToTypeSignature(false), [factory.String]))
            .ImportWith(importer);

        // Change branch targets to instructions
        foreach (var instruction in context.ControlFlowGraph!.Blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.Operands.Count > 0 && instruction.Operands[0] is Block target)
            {
                if (target.Instructions.Count > 0)
                    instruction.Operands[0] = target.Instructions[0];
            }
        }

        var body = new CilMethodBody()
        {
            InitializeLocals = true, // Without this ILSpy does: CompilerServices.Unsafe.SkipInit(out object obj);
            ComputeMaxStackOnBuild = false // There's stack imbalance somewhere, but this works for now
        };

        definition.CilMethodBody = body;

        // Make sure context.Locals actually has all locals (idk why it doesn't sometimes)
        foreach (var operand in context.ControlFlowGraph.Instructions.SelectMany(i => i.Operands))
        {
            LocalVariable? local = null;

            if (operand is FieldReference field)
                local = field.Local;

            if (operand is LocalVariable local2)
                local = local2;

            if (operand is MemoryOperand memory && memory.Base is LocalVariable local3)
                local = local3;

            if (local != null && !context.Locals.Contains(local))
                context.Locals.Add(local);
        }

        // Map ISIL locals to IL
        Dictionary<LocalVariable, CilLocalVariable> locals = [];
        foreach (var local in context.Locals)
        {
            // 'this' aliases are argument 0 (ldarg.0) everywhere; no local slot is needed and
            // declaring one yields an uninitialized `default(T)` local in the decompiled output.
            if (local.IsThis)
                continue;

            TypeSignature ilType;

            // Use object if type couldn't be determined
            if (local.Type != null)
                ilType = local.Type.ToTypeSignature(module);
            else
                ilType = module.CorLibTypeFactory.Object;

            var ilLocal = new CilLocalVariable(ilType);
            body.LocalVariables.Add(ilLocal);
            locals.Add(local, ilLocal);
        }

        // A local that is written but never read produces a bare `_ = value;` statement in the
        // decompiled output. Track which locals are actually read so those dead stores can be
        // dropped at emit time (keeping the value load for stack balance).
        var readLocals = new HashSet<LocalVariable>();
        foreach (var instruction in context.ControlFlowGraph!.Instructions)
        {
            foreach (var source in instruction.Sources)
                if (source is LocalVariable sourceLocal)
                    readLocals.Add(sourceLocal);
        }
        var unusedLocals = context.Locals.Where(l => !readLocals.Contains(l)).ToHashSet();

        /* foreach (var instruction in context.ControlFlowGraph!.Instructions)
        {
            body.Instructions.Add(CilOpCodes.Ldstr, instruction.ToString());
            body.Instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
        }
        body.Instructions.Add(CilOpCodes.Ldstr, "-------------------------------------------------------------------------");
        body.Instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!)); */

        // Generate IL
        Dictionary<Instruction, List<CilInstruction>> instructionMap = [];
        Dictionary<Block, CilInstruction> blockEntryMap = [];
        List<(CilInstruction BranchInstruction, Block TargetBlock)> pendingBlockBranchFixups = [];

        foreach (var block in context.ControlFlowGraph!.Blocks)
        {
            if (block == context.ControlFlowGraph.EntryBlock || block == context.ControlFlowGraph.ExitBlock)
                continue;

            if (block.Instructions.Count == 0)
                continue;

            foreach (var instruction in block.Instructions)
            {
                var generated = GenerateInstructions(instruction, context, definition, locals, unusedLocals, writeLine, stringCtor);
                instructionMap.Add(instruction, generated);

                if (!blockEntryMap.ContainsKey(block) && generated.Count > 0)
                    blockEntryMap[block] = generated[0];
            }

            var lastInstruction = block.Instructions.Last();
            
            if (lastInstruction.OpCode == OpCode.ConditionalJump)
            {
                var trueTarget = TryResolveJumpTargetBlock(lastInstruction, context.ControlFlowGraph);
                var falseSuccessor = block.Successors.FirstOrDefault(s => s != trueTarget && s != context.ControlFlowGraph.ExitBlock);
                if (falseSuccessor == null) continue;
                var bridge = new CilInstruction(CilOpCodes.Br, new CilInstructionLabel());
                definition.CilMethodBody!.Instructions.Add(bridge);
                pendingBlockBranchFixups.Add((bridge, falseSuccessor));
            }

            else if (lastInstruction.OpCode != OpCode.Jump && lastInstruction.OpCode != OpCode.Return && lastInstruction.OpCode != OpCode.IndirectJump)
            {
                var successor = block.Successors.FirstOrDefault(s => s != context.ControlFlowGraph.ExitBlock);
                if (successor == null) continue;
                var bridge = new CilInstruction(CilOpCodes.Br, new CilInstructionLabel());
                definition.CilMethodBody!.Instructions.Add(bridge);
                pendingBlockBranchFixups.Add((bridge, successor));
            }
        }
        // Set IL branch targets
        foreach (var kvp in instructionMap)
        {
            var instruction = kvp.Key;
            var il = kvp.Value;

            if (instruction.OpCode == OpCode.Jump || instruction.OpCode == OpCode.ConditionalJump)
            {
                var ilBranch = il.First(i => i.OpCode == CilOpCodes.Br || i.OpCode == CilOpCodes.Brtrue);

                if (instruction.Operands[0] is Block targetBlock)
                {
                    context.AddWarning($"Branch target block not in cfg: {instruction} ({targetBlock})");
                    ilBranch.OpCode = CilOpCodes.Nop;
                    ilBranch.Operand = null;
                    continue;
                }

                var target = (Instruction)instruction.Operands[0];

                if (!instructionMap.ContainsKey(target))
                {
                    context.AddWarning($"Branch target not in ISIL to IL map: {instruction} --- {target}");
                    ilBranch.OpCode = CilOpCodes.Nop;
                    ilBranch.Operand = null;
                    continue;
                }

                ilBranch.Operand = new CilInstructionLabel(instructionMap[target][0]);
            }
        }
        
        foreach (var (branchInstruction, targetBlock) in pendingBlockBranchFixups)
        {
            var target = ResolveBlockEntryInstruction(targetBlock, blockEntryMap);
            if (target == null)
            {
                context.AddWarning($"Unable to resolve branch target block: {targetBlock}");
                branchInstruction.OpCode = CilOpCodes.Nop;
                branchInstruction.Operand = null;
                continue;
            }

            branchInstruction.Operand = new CilInstructionLabel(target);
        }

        // Add analysis warnings
        var instructions = body.Instructions;
        foreach (var warning in context.AnalysisWarnings)
        {
            instructions.Add(CilOpCodes.Ldstr, "Warning: " + warning);
            instructions.Add(CilOpCodes.Call, importer.ImportMethod(writeLine));
        }
    }
    
    private static Block? TryResolveJumpTargetBlock(Instruction jumpInstruction, ISILControlFlowGraph cfg)
    {
        if (jumpInstruction.Operands.Count == 0)
            return null;

        if (jumpInstruction.Operands[0] is Block targetBlock)
            return targetBlock;

        if (jumpInstruction.Operands[0] is Instruction targetInstruction)
            return cfg.FindBlockByInstruction(targetInstruction);

        return null;
    }

    private static CilInstruction? ResolveBlockEntryInstruction(Block block,
        Dictionary<Block, CilInstruction> blockEntryMap, HashSet<Block>? visited = null)
    {
        if (blockEntryMap.TryGetValue(block, out var target))
            return target;

        visited ??= [];
        if (!visited.Add(block))
            return null;

        foreach (var successor in block.Successors)
        {
            var resolved = ResolveBlockEntryInstruction(successor, blockEntryMap, visited);
            if (resolved != null)
                return resolved;
        }
        return null;
    }

    private static List<CilInstruction> GenerateInstructions(Instruction instruction, MethodAnalysisContext context,
        MethodDefinition method, Dictionary<LocalVariable, CilLocalVariable> locals, HashSet<LocalVariable> unusedLocals,
        MemberReference writeLine, MemberReference stringCtor)
    {
        var body = method.CilMethodBody!;
        var instructions = body.Instructions;
        var currentCount = instructions.Count;
        var startIndex = instructions.Count;

        var module = method.DeclaringModule!;
        var importer = module.DefaultImporter!;

        switch (instruction.OpCode)
        {
            case OpCode.Invalid:
                instructions.Add(CilOpCodes.Ldstr, $"Invalid instruction: {instruction}");
                instructions.Add(CilOpCodes.Call, importer.ImportMethod(writeLine));
                break;

            case OpCode.NotImplemented:
                instructions.Add(CilOpCodes.Ldstr, $"Not implemented instruction: {instruction.Operands[0]}");
                instructions.Add(CilOpCodes.Call, importer.ImportMethod(writeLine));
                break;

            case OpCode.Interrupt:
            case OpCode.Nop:
                instructions.Add(CilOpCodes.Nop);
                break;

            case OpCode.Move:
                // A store into a never-read local is fully dead: emit only a Nop anchor so
                // branch targets stay valid, dropping both the value load and the store.
                if (instruction.Operands[0] is LocalVariable { } deadLocal && unusedLocals.Contains(deadLocal))
                {
                    instructions.Add(CilOpCodes.Nop);
                    break;
                }

                // An unrepresentable destination (complex memory operand or raw register that
                // analysis never promoted to a local) would otherwise force a load + pop pair,
                // which decompiles as a bare `_ = value;`. Drop the whole move as a Nop.
                if (instruction.Operands[0] is not (LocalVariable or FieldReference))
                {
                    instructions.Add(CilOpCodes.Nop);
                    break;
                }

                if (instruction.Operands[0] is FieldReference field) // stfld takes instance before value so LoadOperand StoreToOperand doesn't work
                {
                    var param = method.Parameters.FirstOrDefault(p => p.Name == field.Local.Name);
                    if (param != null)
                        instructions.Add(CilOpCodes.Ldarg, param);
                    else if (field.Local.IsThis)
                        instructions.Add(CilOpCodes.Ldarg_0);
                    else
                        instructions.Add(CilOpCodes.Ldloc, locals[field.Local]);

                    LoadOperand(instruction.Operands[1], method, locals, writeLine, stringCtor);
                    instructions.Add(CilOpCodes.Stfld, field.Field.ToFieldDescriptor(module));
                    break;
                }

                LoadOperand(instruction.Operands[1], method, locals, writeLine, stringCtor);
                StoreToOperand(instruction.Operands[0], method, locals, unusedLocals, writeLine);
                break;

            case OpCode.Newobj:
                // Try and fuse our Newobj + the follow up constructor CallVoid into one IL newobj. 
                // If we can't, just fall back to an Ldnull.
                if (FindConstructorCall(context, instruction) is { Operands: [MethodAnalysisContext constructor, _, ..] } constructorCall)
                {
                    foreach (var argument in constructorCall.Operands.Skip(2))
                        LoadOperand(argument, method, locals, writeLine, stringCtor);

                    instructions.Add(CilOpCodes.Newobj, importer.ImportMethod(constructor.ToMethodDescriptor(module)));
                    StoreToOperand(instruction.Operands[0], method, locals, unusedLocals, writeLine);

                    constructorCall.OpCode = OpCode.Nop;
                    constructorCall.Operands = [];
                }
                else
                {
                    instructions.Add(CilOpCodes.Ldnull);
                    StoreToOperand(instruction.Operands[0], method, locals, unusedLocals, writeLine);
                }
                break;

            case OpCode.Phi:
                instructions.Add(CilOpCodes.Ldstr, $"Phi opcodes should not exist at this point in decompilation ({instruction})");
                instructions.Add(CilOpCodes.Call, importer.ImportMethod(writeLine));
                break;

            case OpCode.Call:
            case OpCode.CallVoid:
                if (instruction.Operands[0] is not MethodAnalysisContext targetMethod)
                {
                    // Keep a physical instruction for potential branch targets. Native
                    // IL2CPP helpers do not have managed metadata, so void helpers
                    // become no-ops and value-producing helpers write a typed default.
                    instructions.Add(CilOpCodes.Nop);
                    if (instruction.OpCode == OpCode.Call && instruction.Operands.Count > 1)
                        StoreUnknownCallDefault(instruction.Operands[1], method, locals, unusedLocals, writeLine);
                    break;
                }

                var importedMethod = importer.ImportMethod(targetMethod.ToMethodDescriptor(module));

                var thisParamIndex = instruction.OpCode == OpCode.Call ? 2 : 1;

                if (!targetMethod.IsStatic) // Load 'this' param
                {
                    if ((instruction.Operands.Count - 1) >= thisParamIndex)
                        LoadOperand(instruction.Operands[thisParamIndex], method, locals, writeLine, stringCtor);
                    else
                    {
                        instructions.Add(CilOpCodes.Ldstr, $"Non static method called without 'this' param ({instruction})");
                        instructions.Add(CilOpCodes.Call, importer.ImportMethod(writeLine));
                    }
                }

                // Load normal params
                var callParamIndex = instruction.OpCode == OpCode.Call ? (targetMethod.IsStatic ? 2 : 3) : (targetMethod.IsStatic ? 1 : 2);
                var callParams = instruction.Operands.Skip(callParamIndex).Take(instruction.Operands.Count - 1 - thisParamIndex);
                foreach (var param in callParams)
                    LoadOperand(param, method, locals, writeLine, stringCtor);

                instructions.Add(CilOpCodes.Call, importedMethod);

                if (instruction.OpCode == OpCode.Call) // Store return value
                    StoreToOperand(instruction.Operands[1], method, locals, unusedLocals, writeLine);

                break;

            case OpCode.IndirectCall:
                // An unresolved indirect call (call through register/vtable). Emit a typed
                // default for the destination if present so the stack stays balanced, and
                // keep a Nop as a physical anchor for any branch targets.
                instructions.Add(CilOpCodes.Nop);
                if (instruction.Operands.Count > 1)
                    StoreUnknownCallDefault(instruction.Operands[1], method, locals, unusedLocals, writeLine);
                break;

            case OpCode.Return:
                if (!context.IsVoid && instruction.Operands.Count == 1)
                    LoadOperand(instruction.Operands[0], method, locals, writeLine, stringCtor);
                instructions.Add(CilOpCodes.Ret);
                break;

            case OpCode.Jump:
                instructions.Add(CilOpCodes.Br, new CilInstructionLabel());
                break;

            case OpCode.ConditionalJump:
                LoadOperand(instruction.Operands[1], method, locals, writeLine, stringCtor);
                instructions.Add(CilOpCodes.Brtrue, new CilInstructionLabel());
                break;

            case OpCode.IndirectJump:
                instructions.Add(CilOpCodes.Ldstr, $"Indirect jump: {instruction} (should have been resolved before IL gen)");
                instructions.Add(CilOpCodes.Call, importer.ImportMethod(writeLine));
                break;

            case OpCode.ShiftStack:
                instructions.Add(CilOpCodes.Ldstr, $"Stack shift: {instruction} (stack analysis should have removed these)");
                instructions.Add(CilOpCodes.Call, importer.ImportMethod(writeLine));
                break;

            case OpCode.CheckEqual:
            case OpCode.CheckGreater:
            case OpCode.CheckLess:
            case OpCode.CheckNotEqual:
            case OpCode.CheckGreaterOrEqual:
            case OpCode.CheckLessOrEqual:

            case OpCode.Add:
            case OpCode.Subtract:
            case OpCode.Multiply:
            case OpCode.Divide:

            case OpCode.ShiftLeft:
            case OpCode.ShiftRight:

            case OpCode.And:
            case OpCode.Or:
            case OpCode.Xor:
                // A computation into a never-read local is fully dead: emit only a Nop anchor
                // so branch targets stay valid, dropping both the operand loads and the store.
                if (instruction.Operands[0] is LocalVariable { } arithDeadLocal && unusedLocals.Contains(arithDeadLocal))
                {
                    instructions.Add(CilOpCodes.Nop);
                    break;
                }

                LoadOperand(instruction.Operands[1], method, locals, writeLine, stringCtor);
                LoadOperand(instruction.Operands[2], method, locals, writeLine, stringCtor);

                switch (instruction.OpCode)
                {
                    case OpCode.CheckEqual: instructions.Add(CilOpCodes.Ceq); break;
                    case OpCode.CheckGreater: instructions.Add(CilOpCodes.Cgt); break;
                    case OpCode.CheckLess: instructions.Add(CilOpCodes.Clt); break;

                    // a != b  ==  (a == b) == 0
                    case OpCode.CheckNotEqual:
                        instructions.Add(CilOpCodes.Ceq);
                        instructions.Add(CilOpCodes.Ldc_I4_0);
                        instructions.Add(CilOpCodes.Ceq);
                        break;
                    // a >= b  ==  !(a < b)
                    case OpCode.CheckGreaterOrEqual:
                        instructions.Add(CilOpCodes.Clt);
                        instructions.Add(CilOpCodes.Ldc_I4_0);
                        instructions.Add(CilOpCodes.Ceq);
                        break;
                    // a <= b  ==  !(a > b)
                    case OpCode.CheckLessOrEqual:
                        instructions.Add(CilOpCodes.Cgt);
                        instructions.Add(CilOpCodes.Ldc_I4_0);
                        instructions.Add(CilOpCodes.Ceq);
                        break;

                    case OpCode.Add: instructions.Add(CilOpCodes.Add); break;
                    case OpCode.Subtract: instructions.Add(CilOpCodes.Sub); break;
                    case OpCode.Multiply: instructions.Add(CilOpCodes.Mul); break;
                    case OpCode.Divide: instructions.Add(CilOpCodes.Div); break;

                    case OpCode.ShiftLeft: instructions.Add(CilOpCodes.Shl); break;
                    case OpCode.ShiftRight: instructions.Add(CilOpCodes.Shr); break;

                    case OpCode.And: instructions.Add(CilOpCodes.And); break;
                    case OpCode.Or: instructions.Add(CilOpCodes.Or); break;
                    case OpCode.Xor: instructions.Add(CilOpCodes.Xor); break;
                }

                StoreToOperand(instruction.Operands[0], method, locals, unusedLocals, writeLine);
                break;

            case OpCode.Not:
            case OpCode.Negate:
                // Dead store: drop the operand load and the store together (stack-neutral).
                if (instruction.Operands[0] is LocalVariable { } unaryDeadLocal && unusedLocals.Contains(unaryDeadLocal))
                {
                    instructions.Add(CilOpCodes.Nop);
                    break;
                }

                LoadOperand(instruction.Operands[1], method, locals, writeLine, stringCtor);

                switch (instruction.OpCode)
                {
                    case OpCode.Not: instructions.Add(CilOpCodes.Not); break;
                    case OpCode.Negate: instructions.Add(CilOpCodes.Neg); break;
                }

                StoreToOperand(instruction.Operands[0], method, locals, unusedLocals, writeLine);
                break;

            default:
                instructions.Add(CilOpCodes.Ldstr, $"Unknown instruction: {instruction}");
                instructions.Add(CilOpCodes.Call, importer.ImportMethod(writeLine));
                break;
        }

        return instructions.ToList().GetRange(startIndex, instructions.Count - startIndex); // Return added IL
    }

    // Try find the follow up CallVoid for a constructor, after a Newobj.
    private static Instruction? FindConstructorCall(MethodAnalysisContext context, Instruction newobj)
    {
        var newObject = newobj.Operands[0];

        foreach (var block in context.ControlFlowGraph!.Blocks)
        {
            var index = block.Instructions.IndexOf(newobj);
            if (index < 0)
                continue;

            for (var i = index + 1; i < block.Instructions.Count; i++)
            {
                var candidate = block.Instructions[i];
                if (candidate is { OpCode: OpCode.CallVoid, Operands: [MethodAnalysisContext { Name: ".ctor" }, _, ..] }
                    && ReferenceEquals(candidate.Operands[1], newObject))
                    return candidate;
            }

            return null;
        }

        return null;
    }

    private static void LoadOperand(object operand, MethodDefinition method,
        Dictionary<LocalVariable, CilLocalVariable> locals, MemberReference writeLine, MemberReference stringCtor)
    {
        var instructions = method.CilMethodBody!.Instructions;

        var module = method.DeclaringModule!;
        var importer = module.DefaultImporter!;

        switch (operand)
        {
            case int i:
                instructions.Add(CilOpCodes.Ldc_I4, i);
                break;
            case uint ui:
                instructions.Add(CilOpCodes.Ldc_I4, unchecked((int)ui));
                break;
            case short s:
                instructions.Add(CilOpCodes.Ldc_I4, s);
                break;
            case ushort us:
                instructions.Add(CilOpCodes.Ldc_I4, us);
                break;
            case byte b8:
                instructions.Add(CilOpCodes.Ldc_I4, b8);
                break;
            case sbyte sb8:
                instructions.Add(CilOpCodes.Ldc_I4, sb8);
                break;
            case long l:
                if (l is >= int.MinValue and <= int.MaxValue)
                    instructions.Add(CilOpCodes.Ldc_I4, (int)l);
                else
                    instructions.Add(CilOpCodes.Ldc_I8, l);
                break;
            case ulong ul:
                if (ul <= int.MaxValue)
                    instructions.Add(CilOpCodes.Ldc_I4, (int)ul);
                else
                    instructions.Add(CilOpCodes.Ldc_I8, unchecked((long)ul));
                break;
            case float f:
                instructions.Add(CilOpCodes.Ldc_R4, f);
                break;
            case double d:
                instructions.Add(CilOpCodes.Ldc_R8, d);
                break;
            case bool b:
                instructions.Add(CilOpCodes.Ldc_I4, b ? 1 : 0);
                break;
            case string s:
                instructions.Add(CilOpCodes.Ldstr, s);
                break;
            case LocalVariable local:
                // Any local aliasing 'this' is argument 0; reading it must be ldarg.0, not an
                // uninitialized local slot.
                if (local.IsThis)
                {
                    instructions.Add(CilOpCodes.Ldarg_0);
                    break;
                }

                var param = method.Parameters.FirstOrDefault(p => p.Name == local.Name);
                if (param != null)
                    instructions.Add(CilOpCodes.Ldarg, param);
                else
                    instructions.Add(CilOpCodes.Ldloc, locals[local]);
                break;
            case FieldReference field:
                // Load the field's declaring instance (this / arg / local), not a hardcoded 'this'.
                if (field.Local.IsThis)
                {
                    instructions.Add(CilOpCodes.Ldarg_0);
                }
                else
                {
                    var fieldParam = method.Parameters.FirstOrDefault(p => p.Name == field.Local.Name);
                    if (fieldParam != null)
                        instructions.Add(CilOpCodes.Ldarg, fieldParam);
                    else
                        instructions.Add(CilOpCodes.Ldloc, locals[field.Local]);
                }
                instructions.Add(CilOpCodes.Ldfld, field.Field.ToFieldDescriptor(module));
                break;
            case MemoryOperand memory:
                if (memory.Index == null && memory.Addend == 0 && memory.Scale == 0
                    && memory.Base is LocalVariable local2)
                {
                    if (local2.IsThis)
                    {
                        instructions.Add(CilOpCodes.Ldarg_0);
                        break;
                    }

                    var param2 = method.Parameters.FirstOrDefault(p => p.Name == local2.Name);
                    if (param2 != null)
                        instructions.Add(CilOpCodes.Ldarg, param2);
                    else
                        instructions.Add(CilOpCodes.Ldloc, locals[local2]);
                    break;
                }
                // The native address is unavailable in managed IL.  Keep the
                // operand stack balanced with a pointer-sized zero instead of
                // emitting a diagnostic call that consumes no value.
                instructions.Add(CilOpCodes.Ldc_I4_0);
                instructions.Add(CilOpCodes.Conv_I);
                break;
            case RuntimeMethodInfoAnalysisContext:
                //Not fully implemented, these basically shouldn't actually ever exist in the final IL.
                instructions.Add(CilOpCodes.Ldc_I4_0);
                instructions.Add(CilOpCodes.Conv_I);
                break;
            case TypeAnalysisContext type:
                // A type operand is a type reference (typeof/ldtoken), not an object
                // construction. Emitting Newobj here created fake `new X()` allocations
                // that never existed in the original code.
                var typeDefOrRef = type.ToTypeSignature(module).ToTypeDefOrRef();
                if (typeDefOrRef == null)
                {
                    instructions.Add(CilOpCodes.Ldnull);
                    break;
                }
                instructions.Add(CilOpCodes.Ldtoken, typeDefOrRef);
                var typeRef = module.CorLibTypeFactory.CorLibScope.CreateTypeReference("System", "Type");
                var getTypeFromHandle = typeRef
                    .CreateMemberReference("GetTypeFromHandle", MethodSignature.CreateStatic(
                        typeRef.ToTypeSignature(false),
                        [module.CorLibTypeFactory.IntPtr]));
                instructions.Add(CilOpCodes.Call, importer.ImportMethod(getTypeFromHandle));
                break;
            default:
                instructions.Add(CilOpCodes.Ldc_I4_0);
                instructions.Add(CilOpCodes.Conv_I);
                break;
        }
    }

    private static void StoreToOperand(object operand, MethodDefinition method,
        Dictionary<LocalVariable, CilLocalVariable> locals, HashSet<LocalVariable> unusedLocals, MemberReference writeLine)
    {
        var instructions = method.CilMethodBody!.Instructions;

        var module = method.DeclaringModule!;
        var importer = module.DefaultImporter!;

        switch (operand)
        {
            case LocalVariable local:
                // 'this' is an argument, not a storable local; writing to it is a no-op.
                if (local.IsThis)
                {
                    instructions.Add(CilOpCodes.Pop);
                    break;
                }
                // Dead store: the value is never read, so drop the store and pop the value
                // load that precedes it, keeping the stack balanced.
                if (unusedLocals.Contains(local))
                {
                    instructions.Add(CilOpCodes.Pop);
                    break;
                }
                instructions.Add(CilOpCodes.Stloc, locals[local]);
                break;

            case FieldReference field:
                // Load the field's declaring instance (this / arg / local), not a hardcoded 'this'.
                if (field.Local.IsThis)
                {
                    instructions.Add(CilOpCodes.Ldarg_0);
                }
                else
                {
                    var fieldParam = method.Parameters.FirstOrDefault(p => p.Name == field.Local.Name);
                    if (fieldParam != null)
                        instructions.Add(CilOpCodes.Ldarg, fieldParam);
                    else
                        instructions.Add(CilOpCodes.Ldloc, locals[field.Local]);
                }
                instructions.Add(CilOpCodes.Stfld, field.Field.ToFieldDescriptor(module));
                break;

            case MemoryOperand memory:
                if (memory.Index == null && memory.Addend == 0 && memory.Scale == 0
                    && memory.Base is LocalVariable local2)
                {
                    // Can pointer assignments just be ignored because it's C#? (Move [local], 123)
                    if (local2.IsThis)
                    {
                        instructions.Add(CilOpCodes.Pop);
                        break;
                    }
                    instructions.Add(CilOpCodes.Stloc, locals[local2]);
                    break;
                }
                // An unresolvable memory destination (indexed/complex) can't be represented in
                // managed IL; drop the store and pop the value that was loaded for it.
                instructions.Add(CilOpCodes.Pop);
                break;

            default:
                // Unrepresentable destination: drop the store, pop the loaded value.
                instructions.Add(CilOpCodes.Pop);
                break;
        }
    }

    private static void StoreUnknownCallDefault(object destination, MethodDefinition method,
        Dictionary<LocalVariable, CilLocalVariable> locals, HashSet<LocalVariable> unusedLocals, MemberReference writeLine)
    {
        var instructions = method.CilMethodBody!.Instructions;
        var module = method.DeclaringModule!;

        // If the destination is a never-read local, the typed default + store pair is dead
        // (the unresolved Call emitted only a Nop, so skipping both keeps the stack balanced).
        if (destination is LocalVariable deadLocal && unusedLocals.Contains(deadLocal))
            return;

        var type = destination switch
        {
            LocalVariable local => local.Type,
            FieldReference field => field.Field.FieldType,
            _ => null
        };

        if (type == null)
            return;

        if (type is RuntimeClassTypeAnalysisContext or RuntimeMethodInfoAnalysisContext or PointerTypeAnalysisContext or ByRefTypeAnalysisContext)
        {
            instructions.Add(CilOpCodes.Ldc_I4_0);
            instructions.Add(CilOpCodes.Conv_I);
        }
        else if (!type.IsValueType)
        {
            instructions.Add(CilOpCodes.Ldnull);
        }
        else if (type.Type is LibCpp2IL.BinaryStructures.Il2CppTypeEnum.IL2CPP_TYPE_I8 or LibCpp2IL.BinaryStructures.Il2CppTypeEnum.IL2CPP_TYPE_U8)
        {
            instructions.Add(CilOpCodes.Ldc_I8, 0L);
        }
        else if (type.Type == LibCpp2IL.BinaryStructures.Il2CppTypeEnum.IL2CPP_TYPE_R4)
        {
            instructions.Add(CilOpCodes.Ldc_R4, 0f);
        }
        else if (type.Type == LibCpp2IL.BinaryStructures.Il2CppTypeEnum.IL2CPP_TYPE_R8)
        {
            instructions.Add(CilOpCodes.Ldc_R8, 0d);
        }
        else if (type.Type is LibCpp2IL.BinaryStructures.Il2CppTypeEnum.IL2CPP_TYPE_I or LibCpp2IL.BinaryStructures.Il2CppTypeEnum.IL2CPP_TYPE_U)
        {
            instructions.Add(CilOpCodes.Ldc_I4_0);
            instructions.Add(CilOpCodes.Conv_I);
        }
        else if (type.Type is LibCpp2IL.BinaryStructures.Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE or LibCpp2IL.BinaryStructures.Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
        {
            var signature = type.ToTypeSignature(module);
            var local = new CilLocalVariable(signature);
            method.CilMethodBody.LocalVariables.Add(local);
            instructions.Add(CilOpCodes.Ldloca, local);
            instructions.Add(CilOpCodes.Initobj, signature.ToTypeDefOrRef());
            instructions.Add(CilOpCodes.Ldloc, local);
        }
        else
        {
            instructions.Add(CilOpCodes.Ldc_I4_0);
        }

        StoreToOperand(destination, method, locals, unusedLocals, writeLine);
    }

}

