namespace Cpp2IL.Core.ISIL;

public enum IsilMnemonic
{
    Move,
    LoadAddress,
    Call,
    CallNoReturn,
    Exchange,
    Add,
    Subtract,
    Multiply,
    Divide,
    ShiftLeft,
    ShiftRight,
    And,
    Or,
    Xor,
    Not,
    Neg,
    Compare,
    ShiftStack,
    Push,
    Pop,
    Return,
    Goto,
    JumpIfEqual,
    JumpIfNotEqual,
    JumpIfGreater,
    JumpIfGreaterOrEqual,
    JumpIfLess,
    JumpIfLessOrEqual,
    JumpIfSign,
    JumpIfNotSign,
    SignExtend,
    Interrupt,
    Nop,
    NotImplemented,
    Invalid,
    VirtualCall,
    AssignIfNotEqual,
    AssignIfEqual,
    AssignIfLessThan,
    AssignIfGreaterOrEqual, //>=
    AssignIfGreaterThan, //>
    Cast2BaseType,//cat to base type
    VectorElementLoad, //Vector element access
    VectorElementStore, //Vector element store
    FABD,
    FSQRT, //Math operations like FSQRT, FMIN, etc.
    FMIN,
    MOVK,
    MADD,
    BFM,
}
