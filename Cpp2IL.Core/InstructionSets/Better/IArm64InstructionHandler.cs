using Disarm;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.InstructionSets.Better;

/// <summary>
/// ARM64指令处理器接口
/// </summary>
public interface IArm64InstructionHandler
{
    /// <summary>
    /// 判断处理器是否能处理指定指令
    /// </summary>
    /// <param name="instruction">ARM64指令</param>
    /// <returns>能否处理</returns>
    bool CanHandle(Arm64Instruction instruction);
    
    /// <summary>
    /// 处理指令并生成ISIL代码
    /// </summary>
    /// <param name="instruction">ARM64指令</param>
    /// <param name="builder">ISIL构建器</param>
    /// <param name="context">方法分析上下文</param>
    void Process(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context);
} 