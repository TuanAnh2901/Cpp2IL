using System;
using Cpp2IL.Core.InstructionSets.Better.Flags;
using Disarm;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Logging;
using Disarm.InternalDisassembly;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.InstructionSets.Better;

/// <summary>
/// 内存操作指令处理器，负责处理加载和存储指令
/// </summary>
public class MemoryOperationHandler : BaseArm64InstructionHandler
{
    public MemoryOperationHandler(FlagsStateManager flagsManager,BetterArmV8InstructionSet set) : base(flagsManager,set)
    {
    }

    public override bool CanHandle(Arm64Instruction instruction)
    {
        return instruction.Mnemonic switch
        {
            // 加载指令
            Arm64Mnemonic.LDR or Arm64Mnemonic.LDUR or
                Arm64Mnemonic.LDRB or Arm64Mnemonic.LDRH or
                Arm64Mnemonic.LDRSW or Arm64Mnemonic.LDP => true,
            Arm64Mnemonic.LDURH => true, // 处理 LDURH 指令
            // 存储指令
            Arm64Mnemonic.STR or Arm64Mnemonic.STUR or
                Arm64Mnemonic.STRB or Arm64Mnemonic.STRH or
                Arm64Mnemonic.STP => true,

            _ => false
        };
    }

    public override bool Process(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.LDURH:
            {
                ProcessUnsignedLoad(instruction, builder, Il2CppTypeEnum.IL2CPP_TYPE_U2);
                break;
            }
            // 所有加载指令统一处理
            case Arm64Mnemonic.LDR:
            case Arm64Mnemonic.LDUR:
            case Arm64Mnemonic.LDRB:
            case Arm64Mnemonic.LDRH:
            case Arm64Mnemonic.LDRSW:
                ProcessSingleLoad(instruction, builder);
                break;

            // 加载对指令
            case Arm64Mnemonic.LDP:
                ProcessLoadPair(instruction, builder);
                break;

            // 所有单个存储指令统一处理
            case Arm64Mnemonic.STR:
            case Arm64Mnemonic.STUR:
            case Arm64Mnemonic.STRB:
            case Arm64Mnemonic.STRH:
                ProcessSingleStore(instruction, builder);
                break;

            // 存储对指令
            case Arm64Mnemonic.STP:
                ProcessStorePair(instruction, builder);
                break;

            default:
                throw new NotImplementedException($"内存操作指令 {instruction.Mnemonic} 尚未实现");
        }

        return false;
    }

    private void ProcessUnsignedLoad(Arm64Instruction instruction, IsilBuilder builder,Il2CppTypeEnum cppTypeEnum)
    {
        var address = instruction.Address;
        var memInfo = GetMemoryAccessInfo(instruction);
        var src = CreateBaseIndexMode(memInfo,builder);
        if (memInfo.IndexMode == Arm64MemoryIndexMode.PreIndex)
        {
            src = ApplyPreIndex(builder, memInfo); //如果是前索引模式 覆盖src的值
        }
        // // 执行加载
        var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
        builder.Move(address, temp, (InstructionSetIndependentOperand)src!);
        
        builder.CastType( address, ConvertOperand(instruction, 0),
            temp, InstructionSetIndependentOperand.MakeCastType(cppTypeEnum));
        // // 处理后索引模式 - 在访问内存后更新基址寄存器
        if (memInfo.IndexMode == Arm64MemoryIndexMode.PostIndex)
        {
            ApplyPostIndex(builder, memInfo);
        }
    }

    /// <summary>
    /// 处理单一加载指令 (LDR, LDUR, LDRB, LDRH, LDRSW)
    /// </summary>
    private void ProcessSingleLoad(Arm64Instruction instruction, IsilBuilder builder)
    {
        var address = instruction.Address;
        var dest = ConvertOperand(instruction, 0);

        // 根据指令获取正确的内存访问模式
        var memInfo = GetMemoryAccessInfo(instruction);

        var src = CreateBaseIndexMode(memInfo,builder);

        if (memInfo.IndexMode == Arm64MemoryIndexMode.PreIndex)
        {
            src = ApplyPreIndex(builder, memInfo); //如果是前索引模式 覆盖src的值
        }

        // // 执行加载
        builder.Move(address, dest, (InstructionSetIndependentOperand)src!);
        //
        // // 处理后索引模式 - 在访问内存后更新基址寄存器
        if (memInfo.IndexMode == Arm64MemoryIndexMode.PostIndex)
        {
            ApplyPostIndex(builder, memInfo);
            // throw new Exception(" not support yet " + memInfo);
            // ApplyIndexUpdate(instruction, builder, memInfo.BaseRegister, memInfo.Offset, false);
        }
    }

    /// <summary>
    /// 处理成对加载指令 (LDP)
    /// </summary>
    private void ProcessLoadPair(Arm64Instruction instruction, IsilBuilder builder)
    {
        var address = instruction.Address;
        var dest1 = ConvertOperand(instruction, 0);
        var dest2 = ConvertOperand(instruction, 1);
        
        // 获取内存访问信息
        var memInfo = GetMemoryAccessInfo(instruction);
        var mem = CreateBaseIndexMode(memInfo,builder);
        if (memInfo.IndexMode == Arm64MemoryIndexMode.PreIndex)
        {
            throw new Exception(" not support LoadPair with PreIndex " + instruction);
        }
        builder.Move(address, dest1, (InstructionSetIndependentOperand)mem!);
        
        var regSize = GetRegisterSizeFromOperand(dest1);
        var memory = mem.Value.Data is IsilMemoryOperand data ? data : default;
        if (memory.Index != null)
        {
            //不支持寄存器的偏移
            throw new Exception("not support ");
        }
        mem = InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(
            memInfo.BaseRegister, memory.Addend + regSize));
        builder.Move(address, dest2, (InstructionSetIndependentOperand)mem!);
    }


    /// <summary>
    /// 处理单一存储指令 (STR, STUR, STRB, STRH)
    /// </summary>
    private void ProcessSingleStore(Arm64Instruction instruction, IsilBuilder builder)
    {
        var address = instruction.Address;
        var src = ConvertOperand(instruction, 0);


        var memInfo = GetMemoryAccessInfo(instruction);


        var dest = CreateBaseIndexMode(memInfo,builder);
        if (memInfo.IndexMode == Arm64MemoryIndexMode.PreIndex)
        {
            dest = ApplyPreIndex(builder, memInfo); //如果是前索引模式 覆盖dest的值
        }

        if (IsZeroReg(src, out var zeroName))
        {
            src = InstructionSetIndependentOperand.MakeRegister(zeroName);
        }

        builder.Move(address, (InstructionSetIndependentOperand)dest!, src);

        if (memInfo.IndexMode == Arm64MemoryIndexMode.PostIndex)
        {
            throw new Exception(" not support yet " + memInfo.IndexMode);
        }
        // // 处理后索引模式
        if (memInfo.IndexMode == Arm64MemoryIndexMode.PostIndex)
        {
            throw new Exception("not support ProcessSingleStore with PostIndex");
            // ApplyIndexUpdate(instruction, builder, memInfo.BaseRegister, memInfo.Offset, false);
        }
    }

    /// <summary>
    /// 处理成对存储指令 (STP)
    /// </summary>
    private void ProcessStorePair(Arm64Instruction instruction, IsilBuilder builder)
    {
        var address = instruction.Address;
        var src1 = ConvertOperand(instruction, 0);
        var src2 = ConvertOperand(instruction, 1);

        if (IsZeroReg(src1, out var zeroName))
        {
            src1 = InstructionSetIndependentOperand.MakeRegister(zeroName);
        }

        if (IsZeroReg(src2, out zeroName))
        {
            src2 = InstructionSetIndependentOperand.MakeRegister(zeroName);
        }

        // // 获取内存访问信息
        var memInfo = GetMemoryAccessInfo(instruction);
        var dest = CreateBaseIndexMode(memInfo,builder);
        if (memInfo.IndexMode == Arm64MemoryIndexMode.PreIndex)
        {
            dest = ApplyPreIndex(builder, memInfo); //如果是前索引模式 覆盖dest的值
        }

        builder.Move(address, (InstructionSetIndependentOperand)dest!, src1);
        var regSize = GetRegisterSizeFromOperand(src1);
        var memory = dest.Value.Data is IsilMemoryOperand data ? data : default;
        if (memory.Index != null)
        {
            //不支持寄存器的偏移
            throw new Exception("not support ");
        }

        dest = InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(
            memInfo.BaseRegister, memory.Addend + regSize));

        builder.Move(address, (InstructionSetIndependentOperand)dest, src2);

        if (memInfo.IndexMode==Arm64MemoryIndexMode.PostIndex)
        {

            throw new Exception("112");
        }
    }

    /// <summary>
    /// 表示内存访问的模式和信息
    /// </summary>
    private class MemoryAccessInfo
    {
        public ulong Address { get; set; }
        public InstructionSetIndependentOperand BaseRegister { get; set; }
        public long Offset { get; set; }
        public InstructionSetIndependentOperand AddendReg { get; set; }
        public bool HasAddendReg { get; set; }
        public Arm64MemoryIndexMode IndexMode { get; set; }
        public Arm64ExtendType ExtendType { get; set; } = Arm64ExtendType.NONE;
        public Arm64ShiftType ShiftType { get; set; } = Arm64ShiftType.NONE;
        public int MemExtendOrShiftAmount { get; set; }


        public override string ToString()
        {
            return $"基址寄存器: {BaseRegister}, 偏移: 0x{Offset.ToString("X")}, 扩展类型: {ExtendType}, " +
                   $"移位类型: {ShiftType}, 移位量: {MemExtendOrShiftAmount}, 索引模式: {IndexMode}";
        }
    }


    /// <summary>
    /// 获取指令的内存访问信息
    /// </summary>
    private MemoryAccessInfo GetMemoryAccessInfo(Arm64Instruction instruction)
    {
        // 获取基址寄存器
        if (instruction.MemBase == Arm64Register.INVALID)
        {
            throw new Exception("基址寄存器无效");
        }

        var baseRegName = instruction.MemBase.ToString().ToUpperInvariant();
        var baseReg = InstructionSetIndependentOperand.MakeRegister(baseRegName);

        // 创建内存访问信息
        var memInfo = new MemoryAccessInfo
        {
            Address = instruction.Address,
            BaseRegister = baseReg,
            Offset = instruction.MemOffset,
            ExtendType = instruction.MemExtendType,
            ShiftType = instruction.MemShiftType,
            MemExtendOrShiftAmount = instruction.MemExtendOrShiftAmount,
            IndexMode = instruction.MemIndexMode
        };
        if (instruction.MemAddendReg != Arm64Register.INVALID) //只有offset 模式才有可能有值
        {
            memInfo.AddendReg =
                InstructionSetIndependentOperand.MakeRegister(instruction.MemAddendReg.ToString().ToUpperInvariant());
            memInfo.HasAddendReg = true;
        }

        return memInfo;
    }

    /// <summary>
    /// 根据内存访问信息创建内存操作数
    /// </summary>
    private InstructionSetIndependentOperand CreateMemoryOperand(MemoryAccessInfo memInfo)
    {
        if (memInfo.IndexMode == Arm64MemoryIndexMode.PreIndex)
        {
            var memoryOperand = new IsilMemoryOperand(memInfo.BaseRegister, memInfo.Offset);
            return InstructionSetIndependentOperand.MakeMemory(memoryOperand);
        }


        throw new Exception(" not support " + memInfo);
        // throw 
    }

    private InstructionSetIndependentOperand ProcessMemoryExtendOrShift(MemoryAccessInfo memory, IsilBuilder builder)
    {
        if (memory.ShiftType!=Arm64ShiftType.NONE)
        {
            var lslReg = InstructionSetIndependentOperand.MakeRegister("TEMP");
            var result = Math.Pow(2, Convert.ToInt64(memory.MemExtendOrShiftAmount));
            if (result.Equals(1))
            {
                //1的话没有意义 因为任何数*1=本身
                return memory.AddendReg;
            }
            builder.Multiply(memory.Address,lslReg,memory.AddendReg,
                InstructionSetIndependentOperand.MakeImmediate(result));
            return  lslReg;
        }
        throw new Exception(" not support yet " + memory.ShiftType +" offset ? "+memory.MemExtendOrShiftAmount);
    }
    
    private InstructionSetIndependentOperand? CreateBaseIndexMode(MemoryAccessInfo memory,IsilBuilder builder)
    {
        if (memory.IndexMode == Arm64MemoryIndexMode.Offset)
        {
            

            if (memory.HasAddendReg)
            {
                //   STR             X20, [X23,X22,LSL#3]
                var addendReg = memory.AddendReg;
                if (memory.ExtendType != Arm64ExtendType.NONE || memory.ShiftType != Arm64ShiftType.NONE)
                {
                    // 处理扩展或移位
                    Logger.InfoNewline("处理扩展或移位 "+memory.MemExtendOrShiftAmount);  
                    addendReg= ProcessMemoryExtendOrShift(memory, builder);
                }
                return InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(memory.BaseRegister,
                    addendReg));
            }

            //没有 那么是立即数 立即数没有拓展或者移位 因为是不合法的
            return InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(memory.BaseRegister,
                memory.Offset));
        }

        if (memory.IndexMode == Arm64MemoryIndexMode.PostIndex)
        {
            //后寻址模式 base的话 肯定是寄存器+偏移
            return InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(memory.BaseRegister));
        }

        return null;
    }

    private void ApplyPostIndex(IsilBuilder builder, MemoryAccessInfo memory)
    {
        if (memory.IndexMode == Arm64MemoryIndexMode.PostIndex)
        {
            builder.Add(memory.Address, memory.BaseRegister, memory.BaseRegister,
                InstructionSetIndependentOperand.MakeImmediate(memory.Offset));
            
        }

    }
    private InstructionSetIndependentOperand ApplyPreIndex(IsilBuilder builder, MemoryAccessInfo memory)
    {
        if (memory.IndexMode == Arm64MemoryIndexMode.PreIndex)
        {
            builder.Add(memory.Address, memory.BaseRegister, memory.BaseRegister,
                InstructionSetIndependentOperand.MakeImmediate(memory.Offset));

            return InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(memory.BaseRegister));
        }

        throw new Exception(" not support ! " + memory.IndexMode + " in ApplyPreIndex");
    }


    /// <summary>
    /// 应用索引更新到基址寄存器
    /// </summary>
    private void ApplyIndexUpdate(Arm64Instruction instruction, IsilBuilder builder,
        InstructionSetIndependentOperand baseReg, long offset, bool isPreIndex)
    {
        var immediateOffset = InstructionSetIndependentOperand.MakeImmediate(offset);

        // 输出调试信息
        // var indexType = isPreIndex ? "前索引" : "后索引";
        // Logger.InfoNewline($"应用{indexType}更新：{baseReg} += {offset}，指令地址：0x{instruction.Address:X}");

        // 更新基址寄存器
        builder.Add(instruction.Address, baseReg, baseReg, immediateOffset);
    }

    /// <summary>
    /// 从操作数获取寄存器大小
    /// </summary>
    private int GetRegisterSizeFromOperand(InstructionSetIndependentOperand operand)
    {
        if (operand.Type != InstructionSetIndependentOperand.OperandType.Register)
            throw new ArgumentException("操作数不是寄存器");

        var regName = ((IsilRegisterOperand)operand.Data).RegisterName;
        return GetRegisterSize(regName);
    }
}
