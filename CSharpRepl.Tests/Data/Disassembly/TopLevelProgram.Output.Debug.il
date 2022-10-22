// Method begins at RVA 0x2050
// Header size: 12
// Code size: 14 (0xe)
.maxstack 2
.entrypoint
.locals init (
    [0] int32,
    [1] int32
)

IL_0000: ldc.i4.5
IL_0001: stloc.0
IL_0002: ldc.i4.3
IL_0003: stloc.1
IL_0004: ldloc.0
IL_0005: ldloc.1
IL_0006: add
IL_0007: call void [System.Console]System.Console::WriteLine(int32)
IL_000c: nop
IL_000d: ret
// Disassembled in Debug Mode. Press Ctrl+F9 to disassemble in Release Mode.
// Compiling code as Console Application (with top-level statements): succeeded.