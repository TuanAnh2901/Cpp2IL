using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.InstructionSets;

public static class InstructionSetIndependentOperandExtensions
{
    public static InstructionSetIndependentOperand FixZero(this InstructionSetIndependentOperand operand,bool useZeroRegister=false)
    {
        if (operand is
            {
                Type: InstructionSetIndependentOperand.OperandType.Register, Data: IsilRegisterOperand registerOperand
            })
        {
            if (registerOperand.IsZeroAlias)
            {
                if (useZeroRegister)
                {
                    return InstructionSetIndependentOperand.MakeRegister(registerOperand.GetZeroRegName());
                }
                return InstructionSetIndependentOperand.MakeImmediate(0);
            }
        }

        return operand;
    }
}
