using Xunit;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Services;

public class ColumnUnitStoreTests
{
    [Fact]
    public void Unset_ReturnsNull()
        => Assert.Null(new InMemoryColumnUnitStore().GetSelectedUnit("RA (J2000.0)"));

    [Fact]
    public void SetGet_RoundTrips_NormalizingKey()
    {
        var store = new InMemoryColumnUnitStore();
        store.SetSelectedUnit("RA (J2000.0)", "degrees");
        Assert.Equal("degrees", store.GetSelectedUnit("ra(j20000)")); // get via cleaned key
    }

    [Fact]
    public void Set_RejectsUnitNotOfferedByColumn()
    {
        var store = new InMemoryColumnUnitStore();
        store.SetSelectedUnit("RA (J2000.0)", "degrees");
        store.SetSelectedUnit("RA (J2000.0)", "lightyears"); // not a valid RA unit
        Assert.Equal("degrees", store.GetSelectedUnit("RA (J2000.0)"));
    }

    [Fact]
    public void SetNull_Clears()
    {
        var store = new InMemoryColumnUnitStore();
        store.SetSelectedUnit("Min Wavelength", "nm");
        store.SetSelectedUnit("Min Wavelength", null);
        Assert.Null(store.GetSelectedUnit("Min Wavelength"));
    }

    [Fact]
    public void ClearAll_Empties()
    {
        var store = new InMemoryColumnUnitStore();
        store.SetSelectedUnit("RA (J2000.0)", "degrees");
        store.SetSelectedUnit("Min Wavelength", "nm");
        store.ClearAll();
        Assert.Null(store.GetSelectedUnit("RA (J2000.0)"));
        Assert.Null(store.GetSelectedUnit("Min Wavelength"));
    }
}
