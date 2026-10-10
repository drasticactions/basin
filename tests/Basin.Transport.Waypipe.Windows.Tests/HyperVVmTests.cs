using Xunit;

namespace Basin.Transport.Waypipe.Windows.Tests;

public sealed class HyperVVmTests
{
    [Theory]
    [InlineData("wsl")]
    [InlineData("WSL")]
    [InlineData(" Wsl ")]
    public void Wsl_is_the_late_resolved_kind(string word)
    {
        var vm = HyperVVm.Parse(word);
        Assert.Equal(HyperVVmKind.Wsl, vm.Kind);
        Assert.Equal(HyperVVm.Wsl, vm);
        Assert.Equal("wsl", vm.ToString());
    }

    [Theory]
    [InlineData("children", "90db8b89-0d35-4f79-8ce9-49ea0ac8b7cd")]
    [InlineData("Wildcard", "00000000-0000-0000-0000-000000000000")]
    [InlineData("LOOPBACK", "e0e16197-dd56-4a10-9195-5ee7a155a838")]
    [InlineData("parent", "a42e7cda-d03f-480c-9cc2-a4de20abb878")]
    public void Each_word_is_its_well_known_guid(string word, string id)
    {
        var vm = HyperVVm.Parse(word);
        Assert.Equal(HyperVVmKind.Fixed, vm.Kind);
        Assert.Equal(Guid.Parse(id), vm.Id);
        Assert.Equal(word.ToLowerInvariant(), vm.ToString());
    }

    [Theory]
    [InlineData("5045efd0-d9fd-4a25-ba73-4a7def558be6")]
    [InlineData("{5045EFD0-D9FD-4A25-BA73-4A7DEF558BE6}")]
    public void A_guid_is_a_fixed_vm(string text)
    {
        var vm = HyperVVm.Parse(text);
        Assert.Equal(HyperVVmKind.Fixed, vm.Kind);
        Assert.Equal(Guid.Parse("5045efd0-d9fd-4a25-ba73-4a7def558be6"), vm.Id);
        Assert.Equal(vm, HyperVVm.Parse(vm.ToString()));
    }

    [Fact]
    public void An_unknown_word_names_every_accepted_one()
    {
        var error = Assert.Throws<FormatException>(() => HyperVVm.Parse("guest"));
        Assert.Contains("'guest'", error.Message, StringComparison.Ordinal);
        Assert.Contains(HyperVVm.Words, error.Message, StringComparison.Ordinal);
    }
}
