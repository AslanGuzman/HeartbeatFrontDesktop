using Heartbeat.Desktop.Localization;
using Heartbeat.Desktop.ViewModels;

namespace Heartbeat.Desktop.Tests;

[Collection(LanguageCollection.Name)]
public sealed class RadiationViewModelTests
{
    [Fact]
    public void Shows_the_simulated_limit_timeline_and_spe_action()
    {
        using TestWorld world = new("ana");
        using RadiationViewModel viewModel = new(world.Store, world.Session, new FakeUrlLauncher());

        Assert.Equal("600 mSv", viewModel.LimitText);
        Assert.Equal(4, viewModel.TimelineRows.Count);
        Assert.True(viewModel.HasSpeAlert);

        viewModel.GoToShelterCommand.Execute(null);

        Assert.Equal(Loc.T("rad.shelterSimulated"), viewModel.ShelterActionText);
    }

    [Fact]
    public void Progress_uses_the_accumulated_total_and_caps_the_visual_bar_at_the_limit()
    {
        using TestWorld world = new("ana");
        using RadiationViewModel viewModel = new(world.Store, world.Session, new FakeUrlLauncher());

        world.Store.AddRadiationDose(world.Session.CurrentUser!.Id, 500m);
        world.Store.AddRadiationDose(world.Session.CurrentUser.Id, 400m);

        Assert.Equal(100d, viewModel.LimitProgress);
        Assert.Equal(Loc.T("rad.percentUsed", 150m), viewModel.PercentText);
    }

    [Fact]
    public void Updates_simulated_labels_when_language_changes()
    {
        using TestWorld world = new("ana");
        using RadiationViewModel viewModel = new(world.Store, world.Session, new FakeUrlLauncher());
        AppLanguage previousLanguage = LocalizationManager.Instance.Language;
        try
        {
            LocalizationManager.Instance.SetLanguage(AppLanguage.Spanish);

            Assert.Contains("simulados", viewModel.TimelineRows[0].TypeText);
            Assert.Contains("utilizado", viewModel.PercentText);
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage(previousLanguage);
        }
    }
}
