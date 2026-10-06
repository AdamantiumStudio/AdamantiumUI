using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// Runtime side of x:ViewModel (Approach X): a view declares its view-model via the <c>ViewModelType</c> metadata
/// (what the codegen emits), and the framework resolves an instance from the app resolver and assigns it as
/// DataContext when the view goes live - via FundamentalUIComponent.OnAttachedToLogicalTree -> ApplyViewModel.
/// Lives here (not UITests) so the process-global UIAppContext.Current isn't shared with the rendering/app tests.
/// </summary>
[TestFixture]
public class ViewModelWiringTests
{
    public class FooViewModel { }

    private sealed class FooView : ContentControl { public override Type ViewModelType => typeof(FooViewModel); }
    private sealed class PlainView : ContentControl { }

    public class CountedViewModel
    {
        public static int Made;

        public CountedViewModel()
        {
            Made++;
        }
    }

    private sealed class CountedView : ContentControl { public override Type ViewModelType => typeof(CountedViewModel); }

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        // ApplyViewModel resolves through UIAppContext.Current.UIContext; provide a minimal context backed by a real
        // container (its Variant-1 fallback auto-creates the unregistered test VM). Idempotent (??=).
        UIAppContext.Initialize(new FakeApp(new AdamantiumDependencyContainer()), null);
    }

    [Test]
    public void NestedView_ResolvesItsViewModelOnAttach()
    {
        var parent = new ContentControl();
        var view = new FooView();

        parent.AddLogicalChild(view);

        Assert.That(view.DataContext, Is.InstanceOf<FooViewModel>());
    }

    [Test]
    public void ViewModelOverridesInheritedDataContext_SiblingWithoutItInherits()
    {
        var parentVm = new object();
        var parent = new ContentControl { DataContext = parentVm };

        var withVm = new FooView();
        var plain = new PlainView();
        parent.AddLogicalChild(withVm);
        parent.AddLogicalChild(plain);

        Assert.That(withVm.DataContext, Is.InstanceOf<FooViewModel>(), "x:ViewModel wins over an inherited DataContext");
        Assert.That(plain.DataContext, Is.SameAs(parentVm), "a view without x:ViewModel still inherits");
    }

    [Test]
    public void AViewBuiltForAViewModel_TakesIt_AndMakesNoSecond()
    {
        CountedViewModel.Made = 0;
        var given = new CountedViewModel();
        CountedView built = null;
        var presenter = new ContentPresenter
        {
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = built = new CountedView() }),
            Content = given
        };
        new ContentControl().AddLogicalChild(presenter);

        presenter.Measure(new Size(100, 100));

        Assert.That(built?.DataContext, Is.SameAs(given));
        Assert.That(CountedViewModel.Made, Is.EqualTo(1), "only the view model the presenter was given");
    }

    [Test]
    public void ANestedViewOfTheSameViewModel_SharesItsParents()
    {
        CountedViewModel.Made = 0;
        var parentVm = new CountedViewModel();
        var parent = new ContentControl { DataContext = parentVm };
        var nested = new CountedView();

        parent.AddLogicalChild(nested);

        Assert.That(nested.DataContext, Is.SameAs(parentVm));
        Assert.That(CountedViewModel.Made, Is.EqualTo(1));
    }

    [Test]
    public void ExplicitDataContextIsNotOverridden()
    {
        var explicitCtx = new object();
        var view = new FooView { DataContext = explicitCtx };

        new ContentControl().AddLogicalChild(view);

        Assert.That(view.DataContext, Is.SameAs(explicitCtx), "an explicit DataContext wins, keeping the view reusable");
    }
}
