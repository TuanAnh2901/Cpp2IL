using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis
{
    public abstract class AsmAnalyzerBase<T> : IAsmAnalyzer
    {
        public IList<T> Instructions => _instructions;
        protected MethodDefinition? MethodDefinition;
        protected ulong MethodEnd;
        protected Il2CppBinary CppAssembly;
        internal List<TypeDefinition> AttributesForRestoration;
        protected bool IsGenuineMethod;
        internal MethodAnalysis<T> Analysis;
        private readonly StringBuilder _methodFunctionality = new();
        protected readonly List<T> _instructions;
        protected BaseKeyFunctionAddresses _keyFunctionAddresses;
        private bool _didFail;
        private int _recoveredInstructionFailures;
        private int _recoveredActionFailures;

        internal AsmAnalyzerBase(ulong methodPointer, IEnumerable<T> instructions, BaseKeyFunctionAddresses keyFunctionAddresses)
        {
            _keyFunctionAddresses = keyFunctionAddresses ?? throw new ArgumentNullException(nameof(keyFunctionAddresses));
            _instructions = new();
            CppAssembly = LibCpp2IlMain.Binary!;

            foreach (var instruction in instructions)
            {
                _instructions.Add(instruction);
            }

            Analysis = new(methodPointer, MethodEnd, keyFunctionAddresses, _instructions);
            Analysis.OnExpansionRequested += AnalysisRequestedExpansion;

            if (FindInstructionWhichOverran(out var idx))
            {
                _instructions = new(_instructions.Take(idx).ToList());
            }

            MethodEnd = _instructions.LastOrDefault().GetNextInstructionAddress();
            if (MethodEnd == 0) MethodEnd = methodPointer;
        }

        internal AsmAnalyzerBase(MethodDefinition definition, ulong methodPointer, IList<T> instructions, BaseKeyFunctionAddresses baseKeyFunctionAddresses) : this(methodPointer, instructions, baseKeyFunctionAddresses)
        {
            MethodDefinition = definition;
            MethodDefinition.Body = new(MethodDefinition);
            IsGenuineMethod = true;
            Analysis = new(definition, methodPointer, MethodEnd, baseKeyFunctionAddresses, _instructions);
            Analysis.OnExpansionRequested += AnalysisRequestedExpansion;
        }

        public StringBuilder GetWordyFunctionality()
        {
            var builder = new StringBuilder();

            builder.Append($"\n\tMethod Synopsis For {(MethodDefinition?.IsStatic == true ? "Static " : "")}Method ")
                .Append(MethodDefinition?.FullName ?? "[unknown name]")
                .Append(":\n").Append((object)_methodFunctionality)
                .Append("\n\n");

            return builder;
        }

        public StringBuilder GetPseudocode()
        {
            var builder = new StringBuilder();

            builder.Append("\n\tGenerated Pseudocode:\n\n");

            //Preamble
            builder.Append($"\tDeclaring Type: {MethodDefinition?.DeclaringType.FullName ?? "unknown"}\n");
            builder.Append('\t').Append(MethodDefinition?.IsStatic == true ? "static " : "").Append(MethodDefinition?.ReturnType.FullName).Append(' ') //Staticness and return type
                .Append(MethodDefinition?.Name).Append('(') //Name and opening paranthesis
                .Append(string.Join(", ", MethodDefinition?.Parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}") ?? new List<string>())) //Parameters
                .Append(')').Append('\n'); //Closing parenthesis and new line.

            //Actions
            Analysis.Actions
                .Where(action => action.IsImportant()) //Action requires pseudocode generation
                .Select(action => $"{(action.PseudocodeNeedsLinebreakBefore() ? "\n" : "")}\t\t{"    ".Repeat(action.IndentLevel)}{action.ToPsuedoCode()?.Replace("\n", "\n" + "    ".Repeat(action.IndentLevel + 2))}") //Generate it 
                .Where(code => !string.IsNullOrWhiteSpace(code)) //Check it's valid
                .ToList()
                .ForEach(code => builder.Append(code).Append('\n')); //Append

            builder.Append("\n\n");

            return builder;
        }

        public StringBuilder BuildILToString()
        {
            var builder = new StringBuilder();

            //IL Generation
            //Anyone reading my commits: This is a *start*. It's nowhere near done.
            var body = MethodDefinition!.Body;
            var processor = body.GetILProcessor();

            var originalBody = body.Instructions.ToList();
            var originalVariables = body.Variables.ToList();

            processor.Clear();

            builder.Append("Generated IL:\n\t");

            var success = !_didFail;

            if (success)
            {
                foreach (var localDefinition in Analysis.Locals.Where(localDefinition => localDefinition.ParameterDefinition == null && localDefinition.Type != null))
                {
                    var varType = localDefinition.Type!;

                    try
                    {
                        if (varType is GenericInstanceType git2 && git2.HasAnyGenericParams())
                            varType = git2.Resolve();
                        if (varType is GenericInstanceType git)
                            varType = processor.ImportRecursive(git, MethodDefinition);
                        if (varType is ArrayType arr)
                        {
                            if(MiscUtils.GetUltimateElementType(arr).IsGenericParameter)
                                throw new InvalidOperationException();
                            if(arr.ElementType is GenericInstanceType arrGit && arrGit.HasAnyGenericParams())
                                varType = arrGit.Resolve().MakeArrayType();
                        }

                        if(varType is ArrayType { ElementType: GenericInstanceType git3})
                            varType = processor.ImportRecursive(git3, MethodDefinition).MakeArrayType();
                        
                        localDefinition.Variable = new VariableDefinition(processor.ImportReference(varType, MethodDefinition));
                        body.Variables.Add(localDefinition.Variable);
                    }
                    catch (InvalidOperationException)
                    {
                        builder.Append($"IL Generation skipped invalid local {localDefinition.Name} of type {localDefinition.Type}\n\t");
                        if (Cpp2IlApi.IlRecoverPartialMethods)
                        {
                            _recoveredActionFailures++;
                            continue;
                        }

                        success = false;
                        break;
                    }
                }
            }

            if (success)
            {
                foreach (var action in Analysis.Actions.Where(i => i.IsImportant()))
                {
                    try
                    {
                        var il = action.ToILInstructions(Analysis, processor);

                        foreach (var instruction in il)
                        {
                            processor.Append(instruction);
                        }

                        if (MethodAnalysis<Instruction>.ActionsWhichGenerateNoIL.Contains(action.GetType()) || il.Length == 0)
                            continue;

                        var jumpsToHere = Analysis.JumpTargetsToFixByAction.Keys.Where(jt => jt.AssociatedInstruction.GetInstructionAddress() <= action.AssociatedInstruction.GetInstructionAddress()).ToList();
                        if (jumpsToHere.Count > 0)
                        {
                            var first = il.First();
                            foreach (var instruction in jumpsToHere.SelectMany(jumpDestAction => Analysis.JumpTargetsToFixByAction[jumpDestAction]))
                            {
                                instruction.Operand = first;
                            }
                        }

                        jumpsToHere.ForEach(key => Analysis.JumpTargetsToFixByAction.Remove(key));
                    }
                    catch (NotImplementedException)
                    {
                        builder.Append($"Don't know how to write IL for {action.GetType()}.");

                        if (!Cpp2IlApi.IlContinueThroughErrors)
                        {
                            builder.Append(" Aborting here.");
                            builder.Append('\n');
                            success = false;
                            break;
                        }

                        if (Cpp2IlApi.IlRecoverPartialMethods)
                            _recoveredActionFailures++;

                        builder.Append("\n\t");
                    }
                    catch (TaintedInstructionException e)
                    {
                        var message = e.ActualMessage ?? "No further info";
                        builder.Append($"Action of type {action.GetType()} at (0x{action.AssociatedInstruction.GetInstructionAddress():X}) is corrupt ({message}) and cannot be created as IL.");
                        if (!Cpp2IlApi.IlContinueThroughErrors)
                        {
                            builder.Append(" Aborting here.");
                            builder.Append('\n');
                            success = false;
                            break;
                        }

                        if (Cpp2IlApi.IlRecoverPartialMethods)
                            _recoveredActionFailures++;

                        builder.Append("\n\t");
                    }
                    catch (Exception e)
                    {
                        Logger.WarnNewline($"Exception generating IL for {MethodDefinition.FullName}, thrown by action {action.GetType().Name}, associated instruction {action.AssociatedInstruction}: {e}");
                        builder.Append($"Action of type {action.GetType()} threw an exception while generating IL.");
                        if (!Cpp2IlApi.IlContinueThroughErrors)
                        {
                            builder.Append(" Aborting here.");
                            builder.Append('\n');
                            success = false;
                            break;
                        }

                        if (Cpp2IlApi.IlRecoverPartialMethods)
                            _recoveredActionFailures++;

                        builder.Append("\n\t");
                    }
                }
            }

            if (body.Variables.Any(l => l.VariableType is GenericParameter { Position: -1 }))
                //don't save to body if any locals are screwed.
                success = false;

            if (Cpp2IlApi.IlRecoverPartialMethods && (_recoveredInstructionFailures > 0 || _recoveredActionFailures > 0))
            {
                AppendPartialRecoveryReturn(body, processor);
                FixUnresolvedJumpTargets(body.Instructions.Last());
                builder.Append($"Partial recovery retained after {_recoveredInstructionFailures} instruction and {_recoveredActionFailures} action failure(s); appended a default return.\n\t");
            }
            else if (body.Instructions.Count > 0 && NeedsTerminalReturn(body.Instructions.Last()))
            {
                // Native methods frequently finish with a call. CIL requires a terminating flow
                // instruction; without this, downstream C# decompilers reject the whole method.
                AppendPartialRecoveryReturn(body, processor);
                FixUnresolvedJumpTargets(body.Instructions.Last());
                builder.Append("Added a missing terminal return.\n\t");
            }

            if (body.Instructions.Count == 0)
                success = false;

            var methodPointerIsUnmapped = !CppAssembly.TryMapVirtualAddressToRaw(Analysis.MethodStart, out _);
            if (!success && Cpp2IlApi.IlRecoverPartialMethods && IsGenuineMethod &&
                (methodPointerIsUnmapped || (!_didFail && Analysis.Actions.Count == 0 &&
                    _recoveredInstructionFailures == 0 && _recoveredActionFailures == 0)))
            {
                // Some metadata entries use an unmapped/shared sentinel instead of a native body.
                // There is no executable code to analyse in that case; attempting to decode its
                // arbitrary low address can create fake actions and ends in AnalysisFailedException.
                // Materialise the CLR-equivalent typed default only in recovery mode: `return;` for
                // void hooks or the default value for value-returning virtual/interface placeholders.
                AppendPartialRecoveryReturn(body, processor);
                success = true;
                builder.Append(methodPointerIsUnmapped
                    ? "Recovered an unmapped native placeholder with a typed default return.\n\t"
                    : "Recovered an empty shared native placeholder with a typed default return.\n\t");
            }

            if (success && Cpp2IlApi.IlRecoverPartialMethods)
            {
                var repairedBranches = RepairConditionalBranchesToBareReturns(body, processor);
                if (repairedBranches > 0)
                    builder.Append($"Redirected {repairedBranches} conditional branch(es) from a bare non-void ret to typed fallback returns.\n\t");
            }

            if (!success && Cpp2IlApi.IlRecoverPartialMethods && IsGenuineMethod)
            {
                // Do not leave a synthetic exception body in a recovery dump. A method that still
                // cannot be expressed after all per-action recovery attempts gets a valid typed
                // fallback body; this keeps the assembly loadable and lets decompilers continue
                // through every type instead of failing at a throw injected by Cpp2IL itself.
                body.Variables.Clear();
                processor.Clear();
                AppendPartialRecoveryReturn(body, processor);
                success = true;
                builder.Append("Applied final typed fallback after unrecoverable IL generation.\n\t");
            }

            if (!success)
            {
                body.Variables.Clear();
                processor.Clear();

                body = new MethodBody(MethodDefinition);
                MethodDefinition.Body = body;

                var failedAnalysisException = MethodDefinition.Module.GetType(AssemblyPopulator.InjectedNamespaceName, "AnalysisFailedException");
                var ctor = failedAnalysisException.Methods.First();

                processor = body.GetILProcessor();

                processor.Emit(OpCodes.Ldstr, "CPP2IL failed to recover any usable IL for this method.");
                processor.Emit(OpCodes.Newobj, ctor);
                processor.Emit(OpCodes.Throw);
            }
            else
            {
                body.InitLocals = true;
                if(IsGenuineMethod)
                    RunILPostProcessors(body);
                body.Optimize();

                builder.Append(string.Join("\n\t", body.Instructions))
                    .Append("\n\t");
            }

            if (IsGenuineMethod)
            {
                if (success)
                    AsmAnalyzerX86.SUCCESSFUL_METHODS++;
                else
                    AsmAnalyzerX86.FAILED_METHODS++;
            }

            builder.Append("\n\n");

            return builder;
        }

        public void BuildMethodFunctionality()
        {
            _methodFunctionality.Append($"\t\tEnd of function at 0x{MethodEnd:X}\n\t\tAbsolute End is at 0x{Analysis.AbsoluteMethodEnd:X}\n");

            _methodFunctionality.Append("\t\tIdentified Jump Destination addresses:\n").Append(string.Join("\n", Analysis.IdentifiedJumpDestinationAddresses.Select(s => $"\t\t\t0x{s:X}"))).Append('\n');
            var lastIfAddress = 0UL;
            foreach (var action in Analysis.Actions)
            {
                if (Analysis.IdentifiedJumpDestinationAddresses.FirstOrDefault(s => s <= action.AssociatedInstruction.GetInstructionAddress() && s > lastIfAddress) is var jumpDestinationAddress && jumpDestinationAddress != 0)
                {
                    var associatedIfForThisElse = Analysis.GetAddressOfAssociatedIfForThisElse(jumpDestinationAddress);
                    var elseStart = Analysis.GetAddressOfElseThisIsTheEndOf(jumpDestinationAddress);
                    var ifStart = Analysis.GetAddressOfIfBlockEndingHere(jumpDestinationAddress);
                    if (associatedIfForThisElse != 0UL)
                    {
                        _methodFunctionality.Append("\n\t\tElse Block (starting at 0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append(") for Comparison at 0x")
                            .Append(associatedIfForThisElse.ToString("x8").ToUpperInvariant())
                            .Append('\n');
                    }
                    else if (elseStart != 0UL)
                    {
                        _methodFunctionality.Append("\n\t\tEnd Of If-Else Block (at 0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append(") where the else started at 0x")
                            .Append(elseStart.ToString("x8").ToUpperInvariant())
                            .Append('\n');
                    }
                    else if (ifStart != 0UL)
                    {
                        _methodFunctionality.Append("\n\t\tEnd Of If Block (at 0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append(") where the if started at 0x")
                            .Append(ifStart.ToString("x8").ToUpperInvariant())
                            .Append('\n');
                    }
                    else
                    {
                        _methodFunctionality.Append("\n\t\tJump Destination (0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append("):\n");
                    }

                    lastIfAddress = jumpDestinationAddress;
                }

                if (Analysis.ProbableLoopStarts.FirstOrDefault(s => s <= action.AssociatedInstruction.GetInstructionAddress() && s > lastIfAddress) is { } loopAddress && loopAddress != 0)
                {
                    _methodFunctionality.Append("\n\t\tPotential Loop Start (0x")
                        .Append(loopAddress.ToString("x8").ToUpperInvariant())
                        .Append("):\n");

                    lastIfAddress = loopAddress;
                }

                string synopsisEntry;
                try
                {
                    synopsisEntry = action.GetSynopsisEntry();
                }
                catch (Exception e)
                {
                    Logger.WarnNewline($"Failed to generate synopsis for method {MethodDefinition?.FullName}, action of type {action.GetType().Name} for instruction {FormatInstruction(action.AssociatedInstruction)} at 0x{action.AssociatedInstruction.GetInstructionAddress():X} - got exception {e}");
                    AsmAnalyzerX86.FAILED_METHODS++;
                    throw new AnalysisExceptionRaisedException("Exception generating synopsis entry", e);
                }

                if (!string.IsNullOrWhiteSpace(synopsisEntry))
                {
                    _methodFunctionality.Append("\t\t0x")
                        .Append(action.AssociatedInstruction.GetInstructionAddress().ToString("X8").ToUpperInvariant())
                        .Append(": ")
                        .Append(action.GetSynopsisEntry())
                        .Append('\n');
                }
            }
        }

        internal void AddParameter(TypeDefinition type, string name)
        {
            Analysis.AddParameter(new(name, ParameterAttributes.None, type));
        }

        public StringBuilder GetFullDumpNoIL()
        {
            var builder = new StringBuilder();

            builder.Append(GetAssemblyDump());
            builder.Append(GetWordyFunctionality());
            builder.Append(GetPseudocode());

            return builder;
        }

        /// <summary>
        /// Performs analysis in order to populate the Action list. Doesn't generate any text. 
        /// </summary>
        /// <exception cref="AnalysisExceptionRaisedException">If an unhandled exception occurs while analyzing.</exception>
        public void AnalyzeMethod()
        {
            //Main instruction loop
            for (var index = 0; index < _instructions.Count; index++)
            {
                var instruction = _instructions[index];
                var actionCountBeforeInstruction = Analysis.Actions.Count;
                try
                {
                    PerformInstructionChecks(instruction);
                }
                catch (Exception e)
                {
                    Logger.WarnNewline($"Failed to perform analysis on method {MethodDefinition?.FullName}\nWhile analysing instruction {FormatInstruction(instruction)} at 0x{instruction.GetInstructionAddress():X}\nGot exception: {e}\n", "Analyze");
                    if (Cpp2IlApi.IlRecoverPartialMethods)
                    {
                        // Some checks add an action before discovering an unsupported operand. Retaining
                        // that half-built action turns one unknown native instruction into invalid IL.
                        if (Analysis.Actions.Count > actionCountBeforeInstruction)
                            Analysis.Actions.RemoveRange(actionCountBeforeInstruction, Analysis.Actions.Count - actionCountBeforeInstruction);

                        _recoveredInstructionFailures++;
                        continue;
                    }

                    _didFail = true;
                    AsmAnalyzerX86.FAILED_METHODS++;
                    throw new AnalysisExceptionRaisedException("Internal analysis exception", e);
                }
            }
        }

        private Mono.Cecil.Cil.Instruction AppendPartialRecoveryReturn(MethodBody body, ILProcessor processor)
        {
            // A skipped action can remove the native return path. Finish the method with valid, typed IL
            // so a C# decompiler can still show the successfully recovered actions.
            var returnType = MethodDefinition!.ReturnType;
            switch (returnType.MetadataType)
            {
                case MetadataType.Void:
                    processor.Emit(OpCodes.Ret);
                    return body.Instructions[^1];
                case MetadataType.Boolean:
                case MetadataType.Char:
                case MetadataType.SByte:
                case MetadataType.Byte:
                case MetadataType.Int16:
                case MetadataType.UInt16:
                case MetadataType.Int32:
                case MetadataType.UInt32:
                    processor.Emit(OpCodes.Ldc_I4_0);
                    processor.Emit(OpCodes.Ret);
                    return body.Instructions[^2];
                case MetadataType.Int64:
                case MetadataType.UInt64:
                    processor.Emit(OpCodes.Ldc_I4_0);
                    processor.Emit(OpCodes.Conv_I8);
                    processor.Emit(OpCodes.Ret);
                    return body.Instructions[^3];
                case MetadataType.Single:
                    processor.Emit(OpCodes.Ldc_R4, 0f);
                    processor.Emit(OpCodes.Ret);
                    return body.Instructions[^2];
                case MetadataType.Double:
                    processor.Emit(OpCodes.Ldc_R8, 0d);
                    processor.Emit(OpCodes.Ret);
                    return body.Instructions[^2];
                case MetadataType.IntPtr:
                case MetadataType.UIntPtr:
                    processor.Emit(OpCodes.Ldc_I4_0);
                    processor.Emit(OpCodes.Conv_I);
                    processor.Emit(OpCodes.Ret);
                    return body.Instructions[^3];
                case MetadataType.Class:
                case MetadataType.Object:
                case MetadataType.String:
                case MetadataType.Array:
                case MetadataType.ByReference:
                case MetadataType.Pointer:
                    processor.Emit(OpCodes.Ldnull);
                    processor.Emit(OpCodes.Ret);
                    return body.Instructions[^2];
                default:
                    var local = new VariableDefinition(processor.ImportReference(returnType, MethodDefinition));
                    body.Variables.Add(local);
                    processor.Emit(OpCodes.Ldloca, local);
                    processor.Emit(OpCodes.Initobj, processor.ImportReference(returnType, MethodDefinition));
                    processor.Emit(OpCodes.Ldloc, local);
                    processor.Emit(OpCodes.Ret);
                    return body.Instructions[^4];
            }
        }

        private int RepairConditionalBranchesToBareReturns(MethodBody body, ILProcessor processor)
        {
            if (MethodDefinition!.ReturnType.MetadataType == MetadataType.Void)
                return 0;

            var repaired = 0;
            foreach (var instruction in body.Instructions.ToList())
            {
                if (instruction.OpCode.FlowControl != FlowControl.Cond_Branch || instruction.Operand is not Mono.Cecil.Cil.Instruction target || target.OpCode.Code != Code.Ret)
                    continue;

                // A conditional branch consumes its condition, so it cannot legally arrive at a
                // non-void ret with no value on the evaluation stack. This appears in obfuscated
                // native control flow after a target action was omitted. Route just that edge to a
                // typed fallback return instead of emitting invalid CIL that rejects in ILSpy/dnSpy.
                instruction.Operand = AppendPartialRecoveryReturn(body, processor);
                repaired++;
            }

            return repaired;
        }

        private void FixUnresolvedJumpTargets(Mono.Cecil.Cil.Instruction fallbackTarget)
        {
            foreach (var pendingJumps in Analysis.JumpTargetsToFixByAction.Values)
            {
                foreach (var jump in pendingJumps)
                    jump.Operand = fallbackTarget;
            }

            Analysis.JumpTargetsToFixByAction.Clear();
        }

        private static bool NeedsTerminalReturn(Mono.Cecil.Cil.Instruction instruction) =>
            instruction.OpCode.FlowControl is not FlowControl.Return and not FlowControl.Throw and not FlowControl.Branch;

        protected abstract bool FindInstructionWhichOverran(out int idx);

        protected abstract void AnalysisRequestedExpansion(ulong ptr);

        internal abstract StringBuilder GetAssemblyDump();

        public abstract void RunActionPostProcessors();
        public abstract void RunILPostProcessors(MethodBody body);

        protected abstract void PerformInstructionChecks(T instruction);

        protected virtual string FormatInstruction(T? instruction) => instruction?.ToString() ?? "null";
    }
}
