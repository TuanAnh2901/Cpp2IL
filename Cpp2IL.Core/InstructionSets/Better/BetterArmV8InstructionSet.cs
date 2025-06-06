using System;
using System.Collections.Generic;
using System.Text;
using Disarm;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.InstructionSets.Better.Flags;
using Cpp2IL.Core.InstructionSets.Better.Handler;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.InstructionSets.Better;

/// <summary>
/// 改进的ARM64指令集实现，使用处理器模式处理指令
/// </summary>
public class BetterArmV8InstructionSet : Cpp2IlInstructionSet
{
    private readonly FlagsStateManager _flagsManager;
    private readonly List<IArm64InstructionHandler> _handlers;

    public BetterArmV8InstructionSet()
    {
        _flagsManager = new FlagsStateManager();

        // 初始化各种指令处理器
        _handlers = new List<IArm64InstructionHandler>
        {
            new MemoryOperationHandler(_flagsManager, this),
            new BranchInstructionHandler(_flagsManager, this),
            new DataProcessingHandler(_flagsManager, this),
            new FlagsProcessHandler(_flagsManager, this),
            new DataConvertHandler(_flagsManager, this)
            // 需要添加其他处理器...
        };
    }

   
    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (context is not ConcreteGenericMethodAnalysisContext)
        {
            // 托管方法或属性生成器 => 获取a和b之间的原始字节范围
            var startOfNextFunction = (int)MiscUtils.GetAddressOfNextFunctionStart(context.UnderlyingPointer);
            var ptrAsInt = (int)context.UnderlyingPointer;
            var count = startOfNextFunction - ptrAsInt;

            if (startOfNextFunction > 0)
                return LibCpp2IlMain.Binary!.GetRawBinaryContent().AsMemory(ptrAsInt, count);
        }

        var result = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer);
        var endVa = result.LastValid().Address + 4;

        var start = (int)context.AppContext.Binary.MapVirtualAddressToRaw(context.UnderlyingPointer);
        var end = (int)context.AppContext.Binary.MapVirtualAddressToRaw(endVa);

        // 合法性检查
        if (start < 0 || end < 0 || start >= context.AppContext.Binary.RawLength ||
            end >= context.AppContext.Binary.RawLength)
            throw new Exception(
                $"无法为方法 {context!.DeclaringType?.FullName}/{context.Name} 将虚拟地址 0x{context.UnderlyingPointer:X} 映射到原始地址 - 起始位置: 0x{start:X}，结束位置: 0x{end:X} 超出了长度为 {context.AppContext.Binary.RawLength} 的范围。");

        return context.AppContext.Binary.GetRawBinaryContent().AsMemory(start, end - start);
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        // 获取ARM64指令
        var instructions = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer);

        foreach (var VARIABLE in instructions)
        {
            Logger.InfoNewline(  VARIABLE.ToString());
        }
        Logger.WarnNewline("Method !=======================> "+context+" <=======================!");
        // 创建ISIL构建器
        var builder = new IsilBuilder();

        // 处理每条指令
        int process = 0;
        int all = instructions.Count;
        foreach (var instruction in instructions)
        {
            if (instruction.Mnemonic==Arm64Mnemonic.INVALID)
            {   
                Logger.InfoNewline("instruction is invalid, stop processing: " + instruction);
                break;
            }
            process++;
            ProcessInstruction(instruction, builder, context);
            Logger.WarnNewline( $"处理指令 {process}/{all} ==> " +instruction);
        }
        Logger.WarnNewline("Method !=======================< End >========================!");
        // 修复跳转地址
        builder.FixJumps();

        return builder.BackingStatementList;
    }

    /// <summary>
    /// 处理单条指令
    /// </summary>
    public void ProcessInstruction(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        // 查找能处理此指令的处理器
        var handler = FindHandler(instruction);

        if (handler != null)
        {
            // 使用找到的处理器处理指令
            var isSetFlag = handler.Process(instruction, builder, context);
            if (isSetFlag)
            {
                var flagHandler = GetFlagsProcessHandler();
                flagHandler.UpdateFlagsState(instruction, builder);
                
            }

            builder.InstructionAddressMap.TryGetValue(instruction.Address, out var instructionAddress);
            var sb = new StringBuilder();
            if (instructionAddress == null && !_flagsManager.IsSetsFlagsInstruction(instruction))
            {
                throw new Exception("处理指令失败! " + instruction);
            }

            if (instructionAddress != null)
            {
                foreach (var VARIABLE in instructionAddress)
                {
                    sb.Append(VARIABLE);
                    sb.Append("; ");
                }

                Logger.WarnNewline("Arm64Instruction 处理结束: " + instruction.ToString() + " ==>  " + sb.ToString());
            }
        }
        else
        {
            // // 没有找到处理器，生成未实现的指令
            // builder.NotImplemented(instruction.Address,
            //     $"指令 {instruction.Mnemonic} 未实现。{instruction}");
            throw   new Exception( $"未找到处理器来处理指令: {instruction.Mnemonic} - {instruction}");
        }
    }

    private FlagsProcessHandler GetFlagsProcessHandler()
    {
        foreach (var handler in _handlers)
        {
            if (handler is FlagsProcessHandler flagsHandler)
                return flagsHandler;
        }

        throw new Exception("FlagsProcessHandler not found");
    }

    /// <summary>
    /// 查找能处理指定指令的处理器
    /// </summary>
    private IArm64InstructionHandler? FindHandler(Arm64Instruction instruction)
    {
        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(instruction))
                return handler;
        }

        return null;
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new NewArm64KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context) => context.RawBytes.Span.Length <= 0
        ? ""
        : string.Join("\n",
            Disassembler.Disassemble(context.RawBytes.Span, context.UnderlyingPointer,
                new Disassembler.Options(true, true, false)).ToList());
}
