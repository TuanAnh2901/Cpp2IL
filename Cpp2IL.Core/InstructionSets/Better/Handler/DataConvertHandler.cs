using System;
using Cpp2IL.Core.InstructionSets.Better.Flags;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Disarm;

namespace Cpp2IL.Core.InstructionSets.Better.Handler;

public class DataConvertHandler : BaseArm64InstructionHandler
{
    public DataConvertHandler(FlagsStateManager flagsManager, BetterArmV8InstructionSet set) : base(flagsManager, set)
    {
    }

    public override bool CanHandle(Arm64Instruction instruction)
    {
        // 处理数据转换指令
        return instruction.Mnemonic switch
        {
            Arm64Mnemonic.SCVTF => true,
            Arm64Mnemonic.FCVT => true,
            Arm64Mnemonic.FCVTZS => true,
            _ => false
        };
    }

    public override bool Process(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        switch (instruction.Mnemonic)
        {
            // FCVT Sd, Dn	双精度（Dn）→ 单精度（Sd）	FCVT S0, D1
            // FCVT Dd, Sn	单精度（Sn）→ 双精度（Dd）	FCVT D0, S1
            case Arm64Mnemonic.FCVT:
            {
                var arg0 = ConvertOperand(instruction, 0);
                var arg1 = ConvertOperand(instruction, 1);
                if (IsSingleRegister(arg0))
                {
                    if (IsDoubleRegister(arg1))
                    {
                        builder.D2F(instruction.Address, arg0, arg1);
                        break;
                    }
                }

                if (IsDoubleRegister(arg0))
                {
                    if (IsSingleRegister(arg1))
                    {
                        builder.F2D(instruction.Address, arg0, arg1);
                        break;
                    }
                }

                throw new Exception($"未支持的类型转换指令 {instruction.Mnemonic} ");
            }
            //有符号整数转换浮点数 支持
            //     SCVTF<Sd> , <Wn > // 32位整数 → 单精度浮点
            //     SCVTF<Dd>, <Wn > // 32位整数 → 双精度浮点
            //     SCVTF<Sd>, <Xn > // 64位整数 → 单精度浮点
            //     SCVTF<Dd>, <Xn > // 64位整数 → 双精度浮点
            case Arm64Mnemonic.SCVTF:
            {
                // 处理浮点数转换
                var arg0 = ConvertOperand(instruction, 0);
                var arg1 = ConvertOperand(instruction, 1);
                if (IsSingleRegister(arg0))
                {
                    if (IsWRegister(arg1))
                    {
                        builder.I42F(instruction.Address, arg0, arg1);
                        break;
                    }

                    if (IsXRegister(arg1))
                    {
                        builder.I82F(instruction.Address, arg0, arg1);
                        break;
                    }

                    if (IsSingleRegister(arg1))
                    {
                        //等同于I42F
                        builder.I42F(instruction.Address, arg0, arg1);
                        break;
                    }
                }

                if (IsDoubleRegister(arg0))
                {
                    if (IsWRegister(arg1))
                    {
                        builder.I42D(instruction.Address, arg0, arg1);
                        break;
                    }

                    if (IsXRegister(arg1))
                    {
                        builder.I82D(instruction.Address, arg0, arg1);
                        break;
                    }

                    if (IsDoubleRegister(arg1))
                    {
                        //等同于I82D
                        builder.I82D(instruction.Address, arg0, arg1);
                        break;
                    }
                }

                throw new Exception($"未支持的类型转换指令 {instruction.Mnemonic} ");
            }
            //浮点数转换有符号整数
            // FCVTZS Wd, Sn	单精度浮点（Sn）→ 32位有符号整数（Wd）	FCVTZS W0, S1
            // FCVTZS Xd, Sn	单精度浮点（Sn）→ 64位有符号整数（Xd）	FCVTZS X0, S1
            // FCVTZS Wd, Dn	双精度浮点（Dn）→ 32位有符号整数（Wd）	FCVTZS W0, D1
            // FCVTZS Xd, Dn	双精度浮点（Dn）→ 64位有符号整数（Xd）	FCVTZS X0, D1
            case Arm64Mnemonic.FCVTZS:
            {
                var arg0 = ConvertOperand(instruction, 0);
                var arg1 = ConvertOperand(instruction, 1);
                if (IsWRegister(arg0))
                {
                    if (IsSingleRegister(arg1))
                    {
                        builder.F2I4(instruction.Address, arg0, arg1);
                        break;
                    }

                    if (IsDoubleRegister(arg1))
                    {
                        builder.D2I4(instruction.Address, arg0, arg1);
                        break;
                    }
                }

                if (IsXRegister(arg0))
                {
                    if (IsSingleRegister(arg1))
                    {
                        builder.F2I8(instruction.Address, arg0, arg1);
                        break;
                    }

                    if (IsDoubleRegister(arg1))
                    {
                        builder.D2I8(instruction.Address, arg0, arg1);
                        break;
                    }
                }
                break;
            }
            default:
                throw new Exception($"未支持的类型转换指令 {instruction.Mnemonic} ");
        }

        return false;
    }
}
