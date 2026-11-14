using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Adamantium.UI.Controls;
using NUnit.Framework;

namespace Adamantium.UITests;

// The definitions built for a type of code loaded to be unloaded again - a game built in an editor - keep nothing of it
// in the builder: once nothing else holds the type, it goes, though the builder lives on.
[TestFixture]
public class PropertyDefinitionBuilderCollectibleTests
{
    [Test]
    public void ACollectibleType_IsLetGo_ByTheBuilderThatBuiltItsDefinitions()
    {
        var builder = new PropertyDefinitionBuilder();
        var gone = BuildAndDrop(builder);

        for (var attempt = 0; attempt < 10 && gone.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(gone.IsAlive, Is.False, "the type is let go");
        GC.KeepAlive(builder);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BuildAndDrop(PropertyDefinitionBuilder builder)
    {
        var type = BuildType();
        var definitions = builder.Build(type);

        Assert.That(definitions, Has.Count.EqualTo(1));
        return new WeakReference(type);
    }

    private static Type BuildType()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Collectible.Inspected"),
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Collectible.Inspected");
        var type = module.DefineType("Game.Patrol", TypeAttributes.Public | TypeAttributes.Class);
        var storage = type.DefineField("_speed", typeof(double), FieldAttributes.Private);
        var property = type.DefineProperty("Speed", PropertyAttributes.None, typeof(double), Type.EmptyTypes);
        const MethodAttributes accessor = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        var getter = type.DefineMethod("get_Speed", accessor, typeof(double), Type.EmptyTypes);
        var getCode = getter.GetILGenerator();
        getCode.Emit(OpCodes.Ldarg_0);
        getCode.Emit(OpCodes.Ldfld, storage);
        getCode.Emit(OpCodes.Ret);

        var setter = type.DefineMethod("set_Speed", accessor, null, [typeof(double)]);
        var setCode = setter.GetILGenerator();
        setCode.Emit(OpCodes.Ldarg_0);
        setCode.Emit(OpCodes.Ldarg_1);
        setCode.Emit(OpCodes.Stfld, storage);
        setCode.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type.CreateType();
    }
}
