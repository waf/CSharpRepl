.class private auto ansi '<Module>'
{
} // end of class <Module>

.class private auto ansi sealed beforefieldinit Microsoft.CodeAnalysis.EmbeddedAttribute
    extends [System.Runtime]System.Attribute
{
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
        01 00 00 00
    )
    .custom instance void Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor() = (
        01 00 00 00
    )
    // Methods
    .method public hidebysig specialname rtspecialname
        instance void .ctor () cil managed
    {
        // Method begins at RVA 0x2050
        // Header size: 1
        // Code size: 7 (0x7)
        .maxstack 8

        IL_0000: ldarg.0
        IL_0001: call instance void [System.Runtime]System.Attribute::.ctor()
        IL_0006: ret
    } // end of method EmbeddedAttribute::.ctor

} // end of class Microsoft.CodeAnalysis.EmbeddedAttribute

.class private auto ansi sealed beforefieldinit System.Runtime.CompilerServices.RefSafetyRulesAttribute
    extends [System.Runtime]System.Attribute
{
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
        01 00 00 00
    )
    .custom instance void Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor() = (
        01 00 00 00
    )
    .custom instance void [System.Runtime]System.AttributeUsageAttribute::.ctor(valuetype [System.Runtime]System.AttributeTargets) = (
        01 00 02 00 00 00 02 00 54 02 0d 41 6c 6c 6f 77
        4d 75 6c 74 69 70 6c 65 00 54 02 09 49 6e 68 65
        72 69 74 65 64 00
    )
    // Fields
    .field public initonly int32 Version

    // Methods
    .method public hidebysig specialname rtspecialname
        instance void .ctor (
            int32 ''
        ) cil managed
    {
        // Method begins at RVA 0x2058
        // Header size: 1
        // Code size: 14 (0xe)
        .maxstack 8

        IL_0000: ldarg.0
        IL_0001: call instance void [System.Runtime]System.Attribute::.ctor()
        IL_0006: ldarg.0
        IL_0007: ldarg.1
        IL_0008: stfld int32 System.Runtime.CompilerServices.RefSafetyRulesAttribute::Version
        IL_000d: ret
    } // end of method RefSafetyRulesAttribute::.ctor

} // end of class System.Runtime.CompilerServices.RefSafetyRulesAttribute

.class private auto ansi beforefieldinit Panda
    extends [System.Runtime]System.Object
{
    // Fields
    .field private initonly string '<Name>k__BackingField'
    .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
        01 00 00 00
    )

    // Methods
    .method public hidebysig specialname
        instance string get_Name () cil managed
    {
        .custom instance void [System.Runtime]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
            01 00 00 00
        )
        // Method begins at RVA 0x2067
        // Header size: 1
        // Code size: 7 (0x7)
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
        // Method begins at RVA 0x206f
        // Header size: 1
        // Code size: 8 (0x8)
        .maxstack 8

        IL_0000: ldarg.0
        IL_0001: ldarg.1
        IL_0002: stfld string Panda::'<Name>k__BackingField'
        IL_0007: ret
    } // end of method Panda::set_Name

    .method public hidebysig specialname rtspecialname
        instance void .ctor () cil managed
    {
        // Method begins at RVA 0x2078
        // Header size: 1
        // Code size: 7 (0x7)
        .maxstack 8

        IL_0000: ldarg.0
        IL_0001: call instance void [System.Runtime]System.Object::.ctor()
        IL_0006: ret
    } // end of method Panda::.ctor

    // Properties
    .property instance string Name()
    {
        .get instance string Panda::get_Name()
        .set instance void modreq([System.Runtime]System.Runtime.CompilerServices.IsExternalInit) Panda::set_Name(string)
    }

} // end of class Panda

// Disassembled in Release Mode.
// Compiling code as Console Application (with top-level statements): failed.
//   - Program does not contain a static 'Main' method suitable for an entry point
// Compiling code as DLL: succeeded.