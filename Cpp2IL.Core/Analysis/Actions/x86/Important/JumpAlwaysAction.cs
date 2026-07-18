using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class JumpAlwaysAction : BaseAction<Instruction>
    {
        private ulong jumpTarget;
        private bool isIfStatement;
        
        public JumpAlwaysAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;
            
            if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            {
                isIfStatement = true;
                if(!context.IdentifiedJumpDestinationAddresses.Contains(jumpTarget))
                    context.IdentifiedJumpDestinationAddresses.Add(jumpTarget);
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            // The action already records a function-local destination in the analysis model.
            // Emit a real branch and let the common target-fixup pass replace this placeholder.
            // Leaving it unimplemented used to produce a basic block whose final instruction was
            // an ordinary call, which ILSpy rejects as malformed control flow.
            var placeholder = processor.Create(OpCodes.Nop);
            var branch = processor.Create(OpCodes.Br, placeholder);
            context.RegisterInstructionTargetToSwapOut(branch, jumpTarget);
            return new[] {branch};
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an function-local jump destination)" : "")}\n";
        }
    }
}
