namespace Cpp2IL;

public sealed record MethodPointerMapEntry(
    string Assembly,
    string Type,
    string Method,
    string Signature,
    string Pointer,
    string Rva);
