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
    public static void GetRealBranch(Arm64Instruction instruction, out List<Arm64Instruction> extraIns,
        out ulong branchTarget)
    {
        ulong baseAddr = instruction.BranchTarget;
        List<Arm64Instruction> instructions = new();
        
        while (true)
        {
            var bytes = LibCpp2IlMain.Binary!.GetRawBinaryContent().AsSpan((int)baseAddr, 4);

            try
            {
                var list = Disassembler.Disassemble(bytes, baseAddr, new Disassembler.Options(true, true, false)).ToList();
                instructions.Add(list[0]);
                var arm64 = list[0];
                if (arm64.Mnemonic==Arm64Mnemonic.B)
                {   
                    extraIns = instructions;
                    branchTarget = arm64.BranchTarget;
                   return;
                }

                baseAddr += 4;

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
