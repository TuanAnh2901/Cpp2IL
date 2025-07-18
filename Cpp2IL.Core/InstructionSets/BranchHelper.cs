using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using Disarm;
using LibCpp2IL;

namespace Cpp2IL.Core.InstructionSets;

public static class BranchHelper
{


    public static Arm64Instruction? GetArm64Ins(ulong addr)
    {
        var bytes = LibCpp2IlMain.Binary!.GetRawBinaryContent().AsSpan((int)addr, 4);
        var list = Disassembler.Disassemble(bytes, addr, new Disassembler.Options(true, true, false)).ToList();
        if (list.Count == 0)
        {
         
            return null;
        }
        

        return list[0];
    }
    public static void GetRealBranch(Arm64Instruction instruction, out List<Arm64Instruction> extraIns,
        out ulong branchTarget)
    {
        ulong baseAddr = instruction.BranchTarget;
        List<Arm64Instruction> instructions = new();
        var rawStart = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(baseAddr);
        int loopCount = 0;
        while (true)
        {
            var bytes = LibCpp2IlMain.Binary!.GetRawBinaryContent().AsSpan((int)rawStart, 4);
           
            try
            {
                var list = Disassembler.Disassemble(bytes, (ulong)rawStart, new Disassembler.Options(true, true, false)).ToList();
                // Logger.InfoNewline("ins !"+list[0]);
                var arm64 = list[0];
                if (arm64.Mnemonic==Arm64Mnemonic.B)
                {   
                    extraIns = instructions;
                    branchTarget=   LibCpp2IlMain.Binary!.MapRawAddressToVirtual((uint)arm64.BranchTarget);
                  
                   return;
                }
                instructions.Add(list[0]);

                rawStart += 4;
                loopCount++;
              
            }
            catch (Exception e)
            {
                throw new(
                    $"Failed to disassemble method body: {string.Join(", ", bytes.ToArray().Select(b => "0x" + b.ToString("X2")))}",
                    e);
            }
            
        }

    }
}
