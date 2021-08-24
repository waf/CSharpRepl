.class private auto ansi '<Module>'
{
} // end of class <Module>

.class private auto ansi abstract sealed beforefieldinit '<Program>$'
    extends [System.Runtime]System.Object
{
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
        01 00 00 00
    )
    // Methods
    .method private hidebysig static
        void '<Main>$' (
            string[] args
        ) cil managed
    {
        // Method begins at RVA 0x2050
        // Code size 11 (0xb)
        .maxstack 2
        .entrypoint
        .locals init (
            [0] int32
        )

        IL_0000: ldc.i4.5
        IL_0001: ldc.i4.3
        IL_0002: stloc.0
        IL_0003: ldloc.0
        IL_0004: add
        IL_0005: call void [System.Console]System.Console::WriteLine(int32)
        IL_000a: ret
    } // end of method '<Program>$'::'<Main>$'

} // end of class <Program>$

// Disassembled in Release Mode.
// Compiling code as Console Application (with top-level statements): succeeded.