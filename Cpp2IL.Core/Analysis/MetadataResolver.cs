using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using IcedInstruction = Iced.Intel.Instruction;
using IcedInstructionList = Iced.Intel.InstructionList;
using LibCpp2IL;

namespace Cpp2IL.Core.Analysis;

public static class MetadataResolver
{
    public static void ResolveAll(MethodAnalysisContext method)
    {
        ResolveCalls(method);
        ResolveGetter(method);
        ResolveMetadataUsages(method);
    }

    /// <summary>
    /// Resolves <c>Move local, [absoluteAddress]</c> loads of IL2CPP metadata-usage globals into a
    /// strongly-typed operand: a string literal, a <see cref="TypeAnalysisContext"/> (an Il2CppType*/
    /// Il2CppClass* usage) or, for a MethodInfo* usage, a <see cref="RuntimeMethodInfoAnalysisContext"/>
    /// naming the method it refers to (also used to type the local - see <see cref="LocalVariables"/>).
    /// </summary>
    private static void ResolveMetadataUsages(MethodAnalysisContext method)
    {
        var libContext = method.AppContext.LibCpp2IlContext;

        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (instruction.OpCode != OpCode.Move)
                continue;

            // Only an absolute-address load [addr] (no base/index/scale) can be a metadata-usage global.
            if (instruction.Operands[0] is not LocalVariable
                || instruction.Operands[1] is not MemoryOperand { Base: null, Index: null, Scale: 0 } memory)
                continue;

            var address = (ulong)memory.Addend;

            // String literal.
            var stringLiteral = libContext.GetLiteralByAddress(address);
            if (stringLiteral != null)
            {
                instruction.Operands[1] = stringLiteral;
                continue;
            }

            // Type metadata usage (Il2CppType* / Il2CppClass*).
            if (method.DeclaringType is { } declaringType)
            {
                var typeContext = libContext.GetTypeGlobalByAddress(address)?.ToContext(declaringType.AppContext);
                if (typeContext != null)
                {
                    instruction.Operands[1] = typeContext;
                    continue;
                }
            }

            // Method metadata usage (MethodInfo*). On metadata v27+ GetMethodGlobalByAddress can return
            // any global, so confirm it is actually a method before resolving - the resolver's switch
            // throws on other usage kinds.
            var methodUsage = libContext.GetMethodGlobalByAddress(address);
            if (methodUsage?.Type is MetadataUsageType.MethodDef or MetadataUsageType.MethodRef
                && method.AppContext.ResolveContextForMethod(methodUsage) is { DeclaringType: { } methodDeclaringType } methodContext)
                instruction.Operands[1] = new RuntimeMethodInfoAnalysisContext(methodContext, methodDeclaringType.DeclaringAssembly);
        }
    }

    /// <summary>
    /// Replaces every <c>[base + addend]</c> memory operand whose base is a typed local with a
    /// <see cref="FieldReference"/> to the field at that offset. Returns whether any operand was
    /// resolved this pass, so the type/field fixpoint can detect convergence: as more bases become
    /// typed (a field load types its result, which is the base of the next load), more offsets
    /// resolve, so this is re-run until it stops finding new fields.
    /// </summary>
    public static bool ResolveFieldOffsets(MethodAnalysisContext method)
    {
        var changed = false;

        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is not MemoryOperand memory)
                    continue;

                // Has to be [base (local) + addend (field offset)]
                if (memory.Index != null || memory.Scale != 0)
                    continue;

                if (memory.Base is not LocalVariable local || local?.Type == null)
                    continue;

                // A type's Fields list only holds fields declared on that type; instance layout
                // offsets for inherited fields are lower than any direct field's, so walk the
                // base chain to resolve accesses to base-class fields.
                var field = FindFieldAtOffset(local.Type, memory.Addend);

                if (field == null) // TODO: Support nested fields (Field1.Field2.Field3)
                    continue;

                instruction.Operands[i] = new FieldReference(field, local, (int)memory.Addend);
                changed = true;
            }
        }

        return changed;
    }

    // Resolves a call whose target is a per-method init thunk: the thunk body's only call
    // targets a key function (e.g. il2cpp_codegen_initialize_runtime_metadata). Returns whether
    // the call was rewritten to a CallVoid with the key function's string name.
    private static bool ResolveCallViaThunk(MethodAnalysisContext method, Instruction callInstruction,
        ulong target, BaseKeyFunctionAddresses keyFunctionAddresses)
    {
        IcedInstructionList body;
        try
        {
            body = X86Utils.GetMethodBodyAtVirtAddressNew(target, false, method.AppContext.Binary);
        }
        catch (Exception)
        {
            return false;
        }

        if (body == null || body.Count == 0)
            return false;

        foreach (IcedInstruction instruction in body)
        {
            if (instruction.Mnemonic != Iced.Intel.Mnemonic.Call)
                continue;

            var innerTarget = instruction.NearBranchTarget;
            if (innerTarget != 0 && keyFunctionAddresses.IsKeyFunctionAddress(innerTarget))
            {
                HandleKeyFunction(method.AppContext, callInstruction, innerTarget, keyFunctionAddresses);
                if (callInstruction.Operands[0] is string)
                    callInstruction.OpCode = OpCode.CallVoid;
                return true;
            }
        }

        return false;
    }

    private static void ResolveCalls(MethodAnalysisContext method)
    {
        var keyFunctionAddresses = method.AppContext.GetOrCreateKeyFunctionAddresses();

        // Resolve every call instruction, not just the block terminator - a block can contain
        // earlier calls (e.g. an init thunk call followed by the real work), and those would
        // otherwise stay numeric and block init-guard removal.
        foreach (var block in method.ControlFlowGraph!.Blocks)
        {
            foreach (var callInstruction in block.Instructions)
            {
                if (callInstruction.OpCode != OpCode.Call && callInstruction.OpCode != OpCode.CallVoid)
                    continue;

                var dest = callInstruction.Operands[0];

                if (!dest.IsNumeric())
                    continue;

                var target = (ulong)dest;

                if (keyFunctionAddresses.IsKeyFunctionAddress(target))
                {
                    HandleKeyFunction(method.AppContext, callInstruction, target, keyFunctionAddresses);
                    continue;
                }

                //Non-key function call. Try to find a single match
                if (!method.AppContext.MethodsByAddress.TryGetValue(target, out var targetMethods))
                {
                    // Some IL2CPP versions route metadata initialization through per-method thunks
                    // (call thunk; thunk: call il2cpp_codegen_initialize_runtime_metadata; ...; ret).
                    // The thunk address is not itself a key function, so resolve it by disassembling
                    // the thunk body and matching its inner call against the key function set.
                    ResolveCallViaThunk(method, callInstruction, target, keyFunctionAddresses);
                    continue;
                }

                // Duplicated/Shared method bodies are resolved later in ResolveCallsViaMethodInfo/ResolveAmbiguousCalls.
                if (targetMethods is not [{ } singleTargetMethod])
                    continue;

                callInstruction.Operands[0] = singleTargetMethod;
            }
        }

        method.ControlFlowGraph.MergeCallBlocks();
    }

    /// <summary>
    /// Resolves indirect calls through a vtable slot, i.e. <c>call [reg+offset]</c> where the
    /// receiver's type is known. The vtable slot number is the byte offset divided by the pointer
    /// size; the slot's method usage names the concrete method. Runs inside the type/field fixpoint
    /// (see <see cref="LocalVariables.ResolveTypesAndFields"/>) because the receiver only becomes
    /// typed as propagation progresses. Returns whether any call was resolved this pass.
    /// </summary>
    public static bool ResolveVirtualCalls(MethodAnalysisContext method)
    {
        var changed = false;

        // Map each local to the Move instruction that defines it, so a `call vN` can be traced
        // back to `vN = [receiver + slotOffset]` and resolved through the receiver's vtable.
        var definitions = new Dictionary<LocalVariable, Instruction>();
        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (instruction.OpCode == OpCode.Move
                && instruction.Operands.Count >= 2
                && instruction.Operands[0] is LocalVariable destination
                && instruction.Operands[1] is MemoryOperand memory)
                definitions[destination] = instruction;
        }

        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (instruction.OpCode != OpCode.IndirectCall)
                continue;

            // Direct form: call [receiver + slotOffset]
            LocalVariable? receiver = null;
            long slotOffset = 0;
            var isDirect = false;

            if (instruction.Operands[0] is MemoryOperand directMemoryOp)
            {
                // Absolute call target: call [constAddr]. If the address is a key function
                // (e.g. il2cpp_runtime_class_init called directly through its global slot),
                // resolve the call to its name so guard removal can recognize it.
                if (directMemoryOp.Base is null && directMemoryOp.Index is null && directMemoryOp.Scale == 0)
                {
                    var kfa = method.AppContext.GetOrCreateKeyFunctionAddresses();
                    var address = (ulong)directMemoryOp.Addend;
                    if (kfa.IsKeyFunctionAddress(address))
                    {
                        HandleKeyFunction(method.AppContext, instruction, address, kfa);
                        if (instruction.Operands[0] is string)
                            instruction.OpCode = OpCode.CallVoid;
                        changed = true;
                    }
                    continue;
                }

                receiver = directMemoryOp.Base as LocalVariable;
                slotOffset = directMemoryOp.Addend;
                isDirect = receiver != null;
            }

            // Indirect form: call vN, where vN was loaded from [receiver + slotOffset] earlier.
            if (!isDirect)
            {
                if (instruction.Operands[0] is not LocalVariable callTarget
                    || !definitions.TryGetValue(callTarget, out var definition)
                    || definition.Operands[1] is not MemoryOperand sourceMemory)
                    continue;

                // Global function pointer: vN = [constAddr]. If the address is a key function
                // (e.g. il2cpp_runtime_class_init called through a thunk), resolve the call to its
                // name so guard removal can recognize it.
                if (sourceMemory.Base is null && sourceMemory.Index is null && sourceMemory.Scale == 0)
                {
                    var kfa = method.AppContext.GetOrCreateKeyFunctionAddresses();
                    var address = (ulong)sourceMemory.Addend;
                    if (kfa.IsKeyFunctionAddress(address))
                    {
                        HandleKeyFunction(method.AppContext, instruction, address, kfa);
                        if (instruction.Operands[0] is string)
                            instruction.OpCode = OpCode.CallVoid;
                        changed = true;
                    }
                    continue;
                }

                if (sourceMemory.Base is not LocalVariable baseLocal)
                    continue;

                receiver = baseLocal;
                slotOffset = sourceMemory.Addend;
            }

            if (receiver.Type is not { } receiverType)
                continue;

            // The receiver must be a real managed instance; a RuntimeClass pointer is a metadata
            // handle, not an object with a vtable.
            if (receiverType is RuntimeClassTypeAnalysisContext or RuntimeMethodInfoAnalysisContext)
                continue;

            var typeDefinition = receiverType.Definition;
            if (typeDefinition == null)
                continue;

            var slot = (int)(slotOffset / method.AppContext.Binary.PointerSizeBytes);
            if (slot < 0)
                continue;

            var vtable = typeDefinition.VTable;
            if (slot >= vtable.Length || vtable[slot] is not { } usage)
                continue;

            MethodAnalysisContext? resolved = null;

            try
            {
                resolved = method.AppContext.ResolveContextForMethod(usage);
            }
            catch (Exception)
            {
                // Ignore unresolvable vtable entries; leave the call unresolved.
            }

            if (resolved == null)
                continue;

            instruction.OpCode = resolved.IsVoid ? OpCode.CallVoid : OpCode.Call;
            instruction.Operands[0] = resolved;

            // Insert the receiver as the 'this' operand so later passes (type propagation, IL gen)
            // see a well-formed call. Call: [target, retval, this, params...]; CallVoid: [target, this, ...].
            // The remaining argument registers already exist as earlier Move instructions in the
            // method body; re-adding raw Register operands here would post-date SSA substitution,
            // so they are left for the calling-convention resolver's earlier operands if present.
            if (instruction.OpCode == OpCode.Call)
            {
                instruction.Operands.Insert(1, new Register(null, "rax"));
                instruction.Operands.Insert(2, receiver);
            }
            else
            {
                instruction.Operands.Insert(1, receiver);
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Resolves calls whose address maps to more than one method by matching the receiver's known
    /// type against the candidates' declaring types. Runs inside the type/field fixpoint and so
    /// re-fires as receivers become typed - a resolved call types its return value, which can type
    /// the receiver of a further call. Returns whether any call was resolved this pass.
    ///
    /// Conservative by design: it commits only when exactly one non-static candidate's declaring
    /// type matches the receiver's type. Anything still untyped or ambiguous is left for a later
    /// pass, or left unresolved - it never guesses.
    /// </summary>
    public static bool ResolveAmbiguousCalls(MethodAnalysisContext method)
    {
        var changed = false;

        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (!instruction.IsCall)
                continue;

            var target = instruction.Operands[0];

            // A resolved call's target is a method/key-function name; only unresolved ones are still numeric.
            if (!target.IsNumeric())
                continue;

            if (!method.AppContext.MethodsByAddress.TryGetValue((ulong)target, out var candidates) || candidates.Count < 2)
                continue;

            if (GetReceiver(instruction) is not { Type: { } receiverType })
                continue;

            MethodAnalysisContext? match = null;
            var ambiguous = false;

            foreach (var candidate in candidates)
            {
                if (candidate.IsStatic || !ReferenceEquals(candidate.DeclaringType, receiverType))
                    continue;

                if (match != null)
                {
                    ambiguous = true;
                    break;
                }

                match = candidate;
            }

            if (ambiguous || match == null)
                continue;

            instruction.Operands[0] = match;
            changed = true;
        }

        return changed;
    }

    // The receiver ('this') of a call is the first integer-slot argument: operand 1 for CallVoid
    // (after the target), operand 2 for Call (after the target and the return value).
    private static LocalVariable? GetReceiver(Instruction call)
    {
        var index = call.OpCode == OpCode.CallVoid ? 1 : 2;
        return index < call.Operands.Count ? call.Operands[index] as LocalVariable : null;
    }

    /// <summary>
    /// Resolves any Call (theoretically should always be a CallVoid) target directly after a Newobj to a constructor call.
    /// </summary>
    public static bool ResolveConstructorCalls(MethodAnalysisContext method)
    {
        var definitions = new Dictionary<LocalVariable, Instruction>();
        foreach (var instruction in method.ControlFlowGraph!.Instructions)
            if (instruction.Destination is LocalVariable definition)
                definitions[definition] = instruction;

        var changed = false;

        foreach (var instruction in method.ControlFlowGraph.Instructions)
        {
            if (!instruction.IsCall || !instruction.Operands[0].IsNumeric())
                continue;

            if (!method.AppContext.MethodsByAddress.TryGetValue((ulong)instruction.Operands[0], out var candidates))
                continue;

            if (GetReceiver(instruction) is not { } receiver || AllocatedType(receiver, definitions) is not { } allocatedType)
                continue;

            var constructor = candidates.FirstOrDefault(c => !c.IsStatic && c.Name == ".ctor" && ReferenceEquals(c.DeclaringType, allocatedType));
            if (constructor == null)
                continue;

            instruction.Operands[0] = constructor;
            changed = true;
        }

        return changed;
    }

    // Follow SSA copies from a local back to the Newobj that produced the value
    private static TypeAnalysisContext? AllocatedType(LocalVariable local, Dictionary<LocalVariable, Instruction> definitions)
    {
        var visited = new HashSet<LocalVariable>();

        while (visited.Add(local) && definitions.TryGetValue(local, out var definition))
        {
            switch (definition.OpCode)
            {
                case OpCode.Newobj:
                    return (definition.Operands[0] as LocalVariable)?.Type;
                case OpCode.Move when definition.Operands[1] is LocalVariable source:
                    local = source;
                    continue;
            }

            break;
        }

        return null;
    }

    /// <summary>
    /// Resolves calls whose address maps to more than one method by reading the runtime
    /// <c>MethodInfo*</c> the caller passes in, if there is one.
    /// </summary>
    public static bool ResolveCallsViaMethodInfo(MethodAnalysisContext method)
    {
        var changed = false;

        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (!instruction.IsCall)
                continue;

            var target = instruction.Operands[0];

            if (!target.IsNumeric())
                //Already resolved
                continue;

            if (!method.AppContext.MethodsByAddress.TryGetValue((ulong)target, out var candidates) || candidates.Count < 2)
                //Not a managed method at all
                continue;

            if (GetMethodInfoArgument(instruction) is not { RepresentedMethod: { } representedMethod })
                //No MethodInfo to work with
                continue;

            //Try to actually match on the method name so we don't just replace a call with something else.
            var representedBase = BaseMethodOf(representedMethod);
            if (!candidates.Any(candidate => ReferenceEquals(BaseMethodOf(candidate), representedBase)))
                continue;

            instruction.Operands[0] = representedMethod;
            changed = true;
        }

        return changed;
    }

    private static MethodAnalysisContext BaseMethodOf(MethodAnalysisContext method) =>
        method is ConcreteGenericMethodAnalysisContext { BaseMethodContext: { } baseMethod } ? baseMethod : method;

    private static RuntimeMethodInfoAnalysisContext? GetMethodInfoArgument(Instruction call)
    {
        var firstArg = call.OpCode == OpCode.CallVoid ? 1 : 2;

        for (var i = call.Operands.Count - 1; i >= firstArg; i--)
        {
            switch (call.Operands[i])
            {
                case RuntimeMethodInfoAnalysisContext methodInfo:
                    return methodInfo;
                case LocalVariable { Type: RuntimeMethodInfoAnalysisContext methodInfoLocal }:
                    return methodInfoLocal;
            }
        }

        return null;
    }

    private static void HandleKeyFunction(ApplicationAnalysisContext appContext, Instruction instruction, ulong target, BaseKeyFunctionAddresses kFA)
    {
        var method = "";
        if (target == kFA.il2cpp_codegen_initialize_method || target == kFA.il2cpp_codegen_initialize_runtime_metadata)
        {
            if (appContext.MetadataVersion < 27)
            {
                method = nameof(kFA.il2cpp_codegen_initialize_method);
            }
            else
            {
                method = nameof(kFA.il2cpp_codegen_initialize_runtime_metadata);
            }
        }
        else
        {
            var pairs = kFA.Pairs.ToList();
            var key = pairs.FirstOrDefault(pair => pair.Value == target).Key;
            if (key == null)
                return;
            method = key;
        }

        if (method != "")
        {
            instruction.Operands[0] = method;
        }
    }

    // Because of il2cpp fields (like cctor_finished_or_no_cctor) [local @ reg+offset] sometimes can't be resolved, but this works for now
    private static void ResolveGetter(MethodAnalysisContext method)
    {
        if (!method.Name.StartsWith("get_"))
            return;

        // Default get: Return [this @ reg+offset]
        var instructions = method.ControlFlowGraph!.Instructions;
        if (instructions.Count == 1)
        {
            var instr = instructions[0];

            if (instr.OpCode != OpCode.Return
                || instr.Operands.Count < 1
                || instr.Operands[0] is not MemoryOperand memory
                || memory.Index != null || memory.Scale != 0
                || memory.Base is not LocalVariable local)
                return;

            var fieldName = $"<{method.Name[4..]}>k__BackingField";

            var field = method.DeclaringType!.Fields.Find(f => f.Name == fieldName);
            if (field == null)
                return;

            instr.Operands[0] = new FieldReference(field, local, (int)memory.Addend);
        }
    }

    /// <summary>
    /// Finds the field whose instance offset matches <paramref name="offset"/>, searching the type's
    /// own fields first, then walking the base type chain. A type's Fields list only contains fields
    /// declared on it, but instance layout includes inherited fields at lower offsets.
    /// </summary>
    private static FieldAnalysisContext? FindFieldAtOffset(TypeAnalysisContext type, long offset)
    {
        var visited = new HashSet<TypeAnalysisContext>();
        var current = type;

        // A runtime class handle (Il2CppClass<X>) points at the class data, where IL2CPP stores
        // the class's static fields; resolve them against the represented type's fields.
        if (current is RuntimeClassTypeAnalysisContext runtimeClass)
            current = runtimeClass.RepresentedType;

        while (current != null && visited.Add(current))
        {
            var field = current.Fields.FirstOrDefault(f => f.BackingData?.FieldOffset == offset);
            if (field != null)
                return field;

            current = current.BaseType;
        }

        return null;
    }
}
