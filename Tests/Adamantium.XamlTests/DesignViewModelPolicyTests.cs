using System;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The designer runs the application's code, so a view's view-model - and whatever it calls: the network,
/// files, data - is built in the preview only when the view says it is safe (<c>x:CreateInDesignTime="True"</c>). The
/// services it must not run for real are replaced by the application's design-time stand-ins.</summary>
[TestFixture]
public class DesignViewModelPolicyTests
{
    public class SampleViewModel { }

    private sealed class UnvouchedView : ContentControl { public override Type ViewModelType => typeof(SampleViewModel); }

    private sealed class VouchedView : ContentControl
    {
        public override Type ViewModelType => typeof(SampleViewModel);
        public override bool CreateViewModelInDesign => true;
    }

    public interface IFeed { }
    public sealed class LiveFeed : IFeed { }
    public sealed class SampleFeed : IFeed { }

    [OneTimeSetUp]
    public void EnsureAppContext() => UIAppContext.Initialize(new FakeApp(new AdamantiumDependencyContainer()), null);

    [Test]
    public void InTheDesigner_OnlyAVouchedViewGetsItsViewModel()
    {
        var wasDesignMode = Design.IsDesignMode;
        try
        {
            Design.IsDesignMode = true;
            var unvouched = new UnvouchedView();
            var vouched = new VouchedView();
            var parent = new ContentControl();
            parent.AddLogicalChild(unvouched);
            parent.AddLogicalChild(vouched);

            Assert.Multiple(() =>
            {
                Assert.That(unvouched.DataContext, Is.Null, "nothing ran for a view that did not say it was safe");
                Assert.That(vouched.DataContext, Is.InstanceOf<SampleViewModel>());
            });
        }
        finally
        {
            Design.IsDesignMode = wasDesignMode;
        }
    }

    [Test]
    public void TheRunningApplication_BuildsEveryViewModel()
    {
        var view = new UnvouchedView();
        new ContentControl().AddLogicalChild(view);

        Assert.That(view.DataContext, Is.InstanceOf<SampleViewModel>(), "the permission is the designer's, not the application's");
    }

    [Test]
    public void AStandInRegisteredBeforeTheApplication_IsWhatGetsResolved()
    {
        // RegisterDesignServices runs before RegisterServices and relies on this: the first registration wins.
        IContainerRegistry container = new AdamantiumDependencyContainer();
        container.RegisterSingleton<IFeed, SampleFeed>();
        container.RegisterSingleton<IFeed, LiveFeed>();

        Assert.That(((IDependencyResolver)container).Resolve<IFeed>(), Is.InstanceOf<SampleFeed>());
    }

    [Test]
    public void TheDirective_BecomesThePermission()
    {
        var vouched = AumlCodegenHarness.Generate(
            AumlCodegenHarness.WindowHeader + "x:ViewModel=\"TextBlock\" x:CreateInDesignTime=\"True\"><Grid /></Window>", out var errors);
        var unvouched = AumlCodegenHarness.Generate(
            AumlCodegenHarness.WindowHeader + "x:ViewModel=\"TextBlock\"><Grid /></Window>", out var moreErrors);

        Assert.That(errors, Is.Empty, AumlCodegenHarness.Errors(errors));
        Assert.That(moreErrors, Is.Empty, AumlCodegenHarness.Errors(moreErrors));
        Assert.Multiple(() =>
        {
            Assert.That(vouched, Does.Contain("public override bool CreateViewModelInDesign => true;"));
            Assert.That(unvouched, Does.Not.Contain("CreateViewModelInDesign"));
            Assert.That(AumlCodegenHarness.Compile(
                AumlCodegenHarness.WindowHeader + "x:ViewModel=\"TextBlock\" x:CreateInDesignTime=\"True\"><Grid /></Window>"),
                Is.Empty, "the permission must compile against the real base class");
        });
    }
}
