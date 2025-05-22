using System;
using System.Collections.Generic;
using Cpp2IL.Core.InstructionSets.Better.Flags.Processors;

namespace Cpp2IL.Core.InstructionSets.Better.Flags;

public class FlagsProcessorFactory
{
    
    private readonly Dictionary<FlagsProcessorType, IFlagsProcessor> _processors = new();
    
    public FlagsProcessorFactory()
    {
        // 注册各种处理器
        _processors[FlagsProcessorType.Compare] = new CompareProcessor();
        _processors[FlagsProcessorType.Arithmetic] = new ArithmeticProcessor();
        // _processors[FlagsProcessorType.ArithmeticSub] = new ArithmeticSubProcessor();
        // _processors[FlagsProcessorType.Logical] = new LogicalProcessor();
        // _processors[FlagsProcessorType.BitTest] = new BitTestProcessor();
    }
    
    /// <summary>
    /// 获取适用于指定标志位状态的处理器
    /// </summary>
    public IFlagsProcessor GetProcessor(Flags.FlagsState state)
    {
        if (_processors.TryGetValue(state.ProcessorType, out var processor))
            return processor;
        
        throw new Exception(" not found processor for " + state.ProcessorType);
        // // 默认使用比较处理器
        // return _processors[FlagsProcessorType.Compare];
    }

}
