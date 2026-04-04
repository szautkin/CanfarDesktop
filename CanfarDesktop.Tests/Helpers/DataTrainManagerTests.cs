using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Tests.Helpers;

public class DataTrainManagerTests
{
    private static List<DataTrainRow> SampleRows() =>
    [
        new() { Band = "Optical", Collection = "CFHT", Instrument = "MegaCam", Filter = "g", CalibrationLevel = "1", DataProductType = "image", ObservationType = "SCIENCE" },
        new() { Band = "Optical", Collection = "CFHT", Instrument = "MegaCam", Filter = "r", CalibrationLevel = "1", DataProductType = "image", ObservationType = "SCIENCE" },
        new() { Band = "Optical", Collection = "CFHT", Instrument = "WIRCam", Filter = "K", CalibrationLevel = "2", DataProductType = "image", ObservationType = "SCIENCE" },
        new() { Band = "Optical", Collection = "HST", Instrument = "WFC3", Filter = "F160W", CalibrationLevel = "2", DataProductType = "image", ObservationType = "SCIENCE" },
        new() { Band = "Radio", Collection = "JCMT", Instrument = "SCUBA-2", Filter = "850", CalibrationLevel = "1", DataProductType = "image", ObservationType = "SCIENCE" },
        new() { Band = "Radio", Collection = "ALMA", Instrument = "Band6", Filter = "", CalibrationLevel = "2", DataProductType = "spectrum", ObservationType = "CALIBRATION" },
        new() { Band = "Infrared", Collection = "JWST", Instrument = "NIRCAM", Filter = "F150W", CalibrationLevel = "3", DataProductType = "image", ObservationType = "SCIENCE" },
    ];

    [Fact]
    public void Load_PopulatesAllDistinctValues()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        Assert.Equal(3, mgr.AllBands.Count); // Optical, Radio, Infrared
        Assert.Contains("Optical", mgr.AllBands);
        Assert.Contains("Radio", mgr.AllBands);
        Assert.Contains("Infrared", mgr.AllBands);

        Assert.Equal(5, mgr.AllCollections.Count); // ALMA, CFHT, HST, JCMT, JWST
        Assert.Equal(6, mgr.AllInstruments.Count);
    }

    [Fact]
    public void Load_SetsAllAvailableInitially()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        Assert.Equal(3, mgr.AvailableBands.Count);
        Assert.Equal(5, mgr.AvailableCollections.Count);
        Assert.Equal(6, mgr.AvailableInstruments.Count);
    }

    [Fact]
    public void Load_EmptyRows_IsLoadedFalse()
    {
        var mgr = new DataTrainManager();
        mgr.Load([]);
        Assert.False(mgr.IsLoaded);
    }

    [Fact]
    public void Toggle_SelectsBand_FiltersCollections()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        mgr.Toggle(0, "Radio"); // Select Radio band

        Assert.Contains("Radio", mgr.SelectedBands);
        // Only JCMT and ALMA have Radio data
        Assert.Equal(2, mgr.AvailableCollections.Count);
        Assert.Contains("JCMT", mgr.AvailableCollections);
        Assert.Contains("ALMA", mgr.AvailableCollections);
        Assert.DoesNotContain("CFHT", mgr.AvailableCollections);
    }

    [Fact]
    public void Toggle_DeselectsBand_RestoresCollections()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        mgr.Toggle(0, "Radio");
        Assert.Equal(2, mgr.AvailableCollections.Count);

        mgr.Toggle(0, "Radio"); // Deselect
        Assert.Empty(mgr.SelectedBands);
        Assert.Equal(5, mgr.AvailableCollections.Count);
    }

    [Fact]
    public void Toggle_ClearsDownstreamSelections()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        mgr.Toggle(0, "Optical");
        mgr.Toggle(1, "CFHT");
        mgr.Toggle(2, "MegaCam");

        Assert.Contains("CFHT", mgr.SelectedCollections);
        Assert.Contains("MegaCam", mgr.SelectedInstruments);

        // Now change band — downstream should clear
        mgr.Toggle(0, "Radio");
        Assert.Empty(mgr.SelectedCollections);
        Assert.Empty(mgr.SelectedInstruments);
    }

    [Fact]
    public void CascadeFilter_MultipleLevels()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        mgr.Toggle(0, "Optical"); // Band=Optical
        mgr.Toggle(1, "CFHT");   // Collection=CFHT

        // Only MegaCam and WIRCam at CFHT with Optical
        Assert.Equal(2, mgr.AvailableInstruments.Count);
        Assert.Contains("MegaCam", mgr.AvailableInstruments);
        Assert.Contains("WIRCam", mgr.AvailableInstruments);

        mgr.Toggle(2, "MegaCam"); // Instrument=MegaCam
        // Filters: g, r
        Assert.Equal(2, mgr.AvailableFilters.Count);
        Assert.Contains("g", mgr.AvailableFilters);
        Assert.Contains("r", mgr.AvailableFilters);
    }

    [Fact]
    public void Prune_RemovesInvalidDownstreamSelections()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        mgr.Toggle(0, "Optical");
        mgr.Toggle(1, "CFHT");
        Assert.Contains("CFHT", mgr.SelectedCollections);

        // Change to Radio — CFHT is not available under Radio
        mgr.Toggle(0, "Optical"); // deselect Optical
        mgr.Toggle(0, "Radio");   // select Radio

        // CFHT was cleared by Toggle (clears downstream)
        Assert.Empty(mgr.SelectedCollections);
    }

    [Fact]
    public void ClearAll_ResetsEverything()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        mgr.Toggle(0, "Optical");
        mgr.Toggle(1, "CFHT");

        mgr.ClearAll();

        Assert.Empty(mgr.SelectedBands);
        Assert.Empty(mgr.SelectedCollections);
        Assert.Equal(5, mgr.AvailableCollections.Count); // All restored
    }

    [Fact]
    public void BandsAlwaysShowAll()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        mgr.Toggle(1, "CFHT"); // Select collection without selecting band first
        // Bands should still show all
        Assert.Equal(3, mgr.AvailableBands.Count);
    }

    [Fact]
    public void StringProperties_ReturnCommaSeparated()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        mgr.Toggle(0, "Optical");
        mgr.Toggle(0, "Radio");

        Assert.Contains("Optical", mgr.BandsString);
        Assert.Contains("Radio", mgr.BandsString);
        Assert.Contains(",", mgr.BandsString);
    }

    [Fact]
    public void StringProperties_EmptyWhenNoSelection()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        Assert.Equal("", mgr.BandsString);
        Assert.Equal("", mgr.CollectionsString);
    }

    [Fact]
    public void EmptyFilterValues_AreExcluded()
    {
        var rows = new List<DataTrainRow>
        {
            new() { Band = "Radio", Collection = "ALMA", Instrument = "Band6", Filter = "", CalibrationLevel = "2", DataProductType = "spectrum", ObservationType = "CALIBRATION" },
        };

        var mgr = new DataTrainManager();
        mgr.Load(rows);

        Assert.Empty(mgr.AllFilters); // Empty string excluded
        Assert.Single(mgr.AllBands);
    }

    [Fact]
    public void Load_SortedAlphabetically()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        // AllBands should be sorted
        Assert.Equal("Infrared", mgr.AllBands[0]);
        Assert.Equal("Optical", mgr.AllBands[1]);
        Assert.Equal("Radio", mgr.AllBands[2]);
    }

    [Fact]
    public void CascadeFilter_DeepColumns_CalLevel_DataType_ObsType()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());

        mgr.Toggle(0, "Radio"); // Band=Radio (JCMT+ALMA)

        // Cal levels under Radio: 1 (JCMT) and 2 (ALMA)
        Assert.Contains("1", mgr.AvailableCalLevels);
        Assert.Contains("2", mgr.AvailableCalLevels);

        // Data types under Radio: image (JCMT) and spectrum (ALMA)
        Assert.Contains("image", mgr.AvailableDataTypes);
        Assert.Contains("spectrum", mgr.AvailableDataTypes);

        // Obs types under Radio: SCIENCE (JCMT) and CALIBRATION (ALMA)
        Assert.Contains("SCIENCE", mgr.AvailableObsTypes);
        Assert.Contains("CALIBRATION", mgr.AvailableObsTypes);

        // Now filter to ALMA only
        mgr.Toggle(1, "ALMA");
        Assert.Single(mgr.AvailableCalLevels); // only "2"
        Assert.Contains("2", mgr.AvailableCalLevels);
        Assert.Single(mgr.AvailableDataTypes); // only "spectrum"
        Assert.Single(mgr.AvailableObsTypes); // only "CALIBRATION"
    }

    [Fact]
    public void RowCount_ReflectsLoadedData()
    {
        var mgr = new DataTrainManager();
        Assert.Equal(0, mgr.RowCount);

        mgr.Load(SampleRows());
        Assert.Equal(7, mgr.RowCount);
    }

    [Fact]
    public void Refresh_IdempotentOnConsistentState()
    {
        var mgr = new DataTrainManager();
        mgr.Load(SampleRows());
        mgr.Toggle(0, "Optical");

        var bandsBefore = mgr.AvailableBands.Count;
        var collsBefore = mgr.AvailableCollections.Count;

        mgr.Refresh(); // should not change anything

        Assert.Equal(bandsBefore, mgr.AvailableBands.Count);
        Assert.Equal(collsBefore, mgr.AvailableCollections.Count);
    }
}
