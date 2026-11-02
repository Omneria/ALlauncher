using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.ViewModels;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Page Paramètres : chargement, validation de la saisie et application aux réglages.</summary>
public sealed class SettingsViewModelTests
{
    [Fact]
    public void Le_chargement_reprend_les_reglages_et_efface_les_erreurs()
    {
        var settings = new LauncherSettings
        {
            ScreenWidth = 1920,
            ScreenHeight = 1080,
            GameDirectory = @"C:\jeu",
            DesktopNotificationsEnabled = false,
            MinimizeWhileGameRuns = false,
            SoundsEnabled = true,
            FriendNames = ["Nyx_", "Kaelith"],
        };
        var vm = new SettingsViewModel { ScreenWidthText = "abc" };
        vm.TryApply(new LauncherSettings(), 2048, 4096);
        Assert.True(vm.HasValidationError);

        vm.Load(settings);

        Assert.Equal("1920", vm.ScreenWidthText);
        Assert.Equal("1080", vm.ScreenHeightText);
        Assert.Equal(@"C:\jeu", vm.GameDirectory);
        Assert.False(vm.DesktopNotifications);
        Assert.True(vm.Sounds);
        Assert.Equal("Nyx_, Kaelith", vm.FriendsText);
        Assert.False(vm.HasValidationError);
        Assert.False(vm.WidthInvalid);
    }

    [Fact]
    public void Une_saisie_valide_est_appliquee_aux_reglages()
    {
        var settings = new LauncherSettings();
        var vm = new SettingsViewModel();
        vm.Load(settings);
        vm.ScreenWidthText = "1600";
        vm.ScreenHeightText = "900";
        vm.GameDirectory = @"D:\mc";
        vm.DesktopNotifications = false;
        vm.MinimizeWhileGame = false;
        vm.Sounds = true;
        vm.FriendsText = " Alex, sam ,, ALEX";

        Assert.True(vm.TryApply(settings, 2048, 6144));

        Assert.Equal(2048, settings.MinRamMb);
        Assert.Equal(6144, settings.MaxRamMb);
        Assert.Equal(1600, settings.ScreenWidth);
        Assert.Equal(900, settings.ScreenHeight);
        Assert.Equal(@"D:\mc", settings.GameDirectory);
        Assert.False(settings.DesktopNotificationsEnabled);
        Assert.False(settings.MinimizeWhileGameRuns);
        Assert.True(settings.SoundsEnabled);
        Assert.Equal(["Alex", "sam"], settings.FriendNames);
    }

    [Theory]
    [InlineData("abc", "0", true, false)]
    [InlineData("-5", "0", true, false)]
    [InlineData("0", "1.5", false, true)]
    [InlineData("", "", true, true)]
    public void Une_saisie_invalide_nenregistre_rien_et_dit_pourquoi(string width, string height, bool widthInvalid, bool heightInvalid)
    {
        var settings = new LauncherSettings { ScreenWidth = 800, ScreenHeight = 600 };
        var vm = new SettingsViewModel { ScreenWidthText = width, ScreenHeightText = height };

        Assert.False(vm.TryApply(settings, 2048, 6144));

        Assert.Equal(widthInvalid, vm.WidthInvalid);
        Assert.Equal(heightInvalid, vm.HeightInvalid);
        Assert.True(vm.HasValidationError);
        Assert.Equal(800, settings.ScreenWidth);
        Assert.Equal(600, settings.ScreenHeight);
    }

    [Fact]
    public void Un_dossier_vide_ne_remplace_pas_le_dossier_actuel()
    {
        var settings = new LauncherSettings { GameDirectory = @"C:\jeu" };
        var vm = new SettingsViewModel { GameDirectory = "  " };

        Assert.True(vm.TryApply(settings, 2048, 4096));

        Assert.Equal(@"C:\jeu", settings.GameDirectory);
    }

    [Fact]
    public void Les_arguments_JVM_sont_charges_puis_appliques_aux_reglages()
    {
        var settings = new LauncherSettings { ExtraJvmArguments = "-XX:+UseG1GC" };
        var vm = new SettingsViewModel();
        vm.Load(settings);
        Assert.Equal("-XX:+UseG1GC", vm.ExtraJvmArgumentsText);

        vm.ExtraJvmArgumentsText = "-Dfoo=bar   -XX:MaxGCPauseMillis=50";

        Assert.True(vm.TryApply(settings, 2048, 4096));
        Assert.Equal("-Dfoo=bar -XX:MaxGCPauseMillis=50", settings.ExtraJvmArguments);
        Assert.False(vm.JvmArgumentsInvalid);
    }

    [Fact]
    public void Un_argument_JVM_invalide_bloque_l_enregistrement_et_est_signale()
    {
        var settings = new LauncherSettings { ExtraJvmArguments = "-Dold=1" };
        var vm = new SettingsViewModel { ExtraJvmArgumentsText = "-Xmx16G" };

        Assert.False(vm.TryApply(settings, 2048, 4096));

        Assert.True(vm.JvmArgumentsInvalid);
        Assert.Contains("curseur de RAM", vm.ValidationMessage);
        Assert.Equal("-Dold=1", settings.ExtraJvmArguments);
    }
}
