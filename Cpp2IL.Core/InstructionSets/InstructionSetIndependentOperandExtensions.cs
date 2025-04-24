using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.InstructionSets;

public static class InstructionSetIndependentOperandExtensions
{
    public static InstructionSetIndependentOperand FixZero(this InstructionSetIndependentOperand operand)
    {
        if (operand is
            {
                Type: InstructionSetIndependentOperand.OperandType.Register, Data: IsilRegisterOperand registerOperand
            })
        {
            if (registerOperand.IsZeroAlias)
            {
                return InstructionSetIndependentOperand.MakeImmediate(0);
            }
        }

        return operand;
    }
}
