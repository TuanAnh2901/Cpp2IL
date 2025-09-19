using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

public class IsilPrologueStackFrame(int frameSize,ulong address,List<InstructionSetIndependentInstruction> instructions) : IsilOperandData
{



    public ulong Address => address;
    
    public int FrameSize => frameSize;
    public List<InstructionSetIndependentInstruction> Instructions => instructions;
    
    
    
    
    
    public override string ToString()
    {
        return $"Prologue Stack(Size: {FrameSize}, Instructions: {Instructions.Count})";
    }
    
    
    public static IsilPrologueStackFrame Create(int frameSize, ulong address, List<InstructionSetIndependentInstruction> instructions)
    {
        return new IsilPrologueStackFrame(frameSize,address, instructions);
    }
}

public class IsilEpilogueStackFrame(int frameSize,ulong address, List<InstructionSetIndependentInstruction> instructions)
    : IsilOperandData
{
    public ulong Address => address;
    public int FrameSize => frameSize;
    public List<InstructionSetIndependentInstruction> Instructions => instructions;
    
    
    public override string ToString()
    {
        return $"Epilogue Stack(Size: {FrameSize}, Instructions: {Instructions.Count})";
    }
    
    
    public static IsilEpilogueStackFrame Create(int frameSize,ulong address, List<InstructionSetIndependentInstruction> instructions)
    {
        return new IsilEpilogueStackFrame(frameSize,  address,instructions);
    }
}
