.class private auto ansi '<Module>'
{
} // end of class <Module>

.class private auto ansi beforefieldinit Panda
    extends [System.Runtime]System.Object
{
    // Fields
    .field private initonly string '<Name>k__BackingField'
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
        01 00 00 00
    )
    .custom instance void [System.Runtime]System.Diagnostics.DebuggerBrowsableAttribute::.ctor(valuetype [System.Runtime]System.Diagnostics.DebuggerBrowsableState) = (
        01 00 00 00 00 00 00 00
    )

    // Methods
    .method public hidebysig specialname
        instance string get_Name () cil managed
    {
        .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
            01 00 00 00
        )
        // Method begins at RVA 0x2050
        // Code size 7 (0x7)
        .maxstack 8

        IL_0000: ldarg.0
        IL_0001: ldfld string Panda::'<Name>k__BackingField'
        IL_0006: ret
    } // end of method Panda::get_Name

    .method public hidebysig specialname
        instance void modreq([System.Runtime]System.Runtime.CompilerServices.IsExternalInit) set_Name (
            string 'value'
        ) cil managed
    {
        .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
            01 00 00 00
        )
        // Method begins at RVA 0x2058
        // Code size 8 (0x8)
        .maxstack 8

        IL_0000: ldarg.0
        IL_0001: ldarg.1
        IL_0002: stfld string Panda::'<Name>k__BackingField'
        IL_0007: ret
    } // end of method Panda::set_Name

    .method public hidebysig specialname rtspecialname
        instance void .ctor () cil managed
    {
        // Method begins at RVA 0x2061
        // Code size 8 (0x8)
        .maxstack 8

        IL_0000: ldarg.0
        IL_0001: call instance void [System.Runtime]System.Object::.ctor()
        IL_0006: nop
        IL_0007: ret
    } // end of method Panda::.ctor

    // Properties
    .property instance string Name()
    {
        .get instance string Panda::get_Name()
        .set instance void modreq([System.Runtime]System.Runtime.CompilerServices.IsExternalInit) Panda::set_Name(string)
    }

} // end of class Panda

// Disassembled in Debug Mode. Press Ctrl+F9 to disassemble in Release Mode.
// Compiling code as Console Application (with top-level statements): failed.
//   - Program does not contain a static 'Main' method suitable for an entry point
// Compiling code as DLL: succeeded.