using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.Controls;

public sealed partial class ResourceSelectorPanel : UserControl
{
    private int[] _ramPower2Values = [1, 2, 4, 8, 16, 32, 64, 128, 256];
    private bool _syncing;

    public int Cores { get; private set; } = 2;
    public int Ram { get; private set; } = 8;
    public int Gpus { get; private set; }

    public ResourceSelectorPanel()
    {
        InitializeComponent();
    }

    public void Configure(int[] coreOptions, int defaultCores, int[] ramOptions, int defaultRam, int[] gpuOptions)
    {
        _syncing = true;

        // CPU
        if (coreOptions.Length > 0)
        {
            CoresBox.Minimum = coreOptions.Min();
            CoresBox.Maximum = coreOptions.Max();
            CoresSlider.Minimum = coreOptions.Min();
            CoresSlider.Maximum = coreOptions.Max();
        }
        CoresBox.Value = defaultCores;
        CoresSlider.Value = defaultCores;
        Cores = defaultCores;

        // RAM — build power-of-2 scale within the available range
        if (ramOptions.Length > 0)
        {
            var min = ramOptions.Min();
            var max = ramOptions.Max();

            var powers = new List<int>();
            int v = 1;
            while (v <= max)
            {
                if (v >= min) powers.Add(v);
                v *= 2;
            }
            if (powers.Count == 0) powers.AddRange(ramOptions);
            _ramPower2Values = powers.ToArray();

            RamBox.Minimum = min;
            RamBox.Maximum = max;
            RamSlider.Minimum = 0;
            RamSlider.Maximum = _ramPower2Values.Length - 1;
        }

        var defaultIndex = FindNearestPow2Index(defaultRam);
        RamSlider.Value = defaultIndex;
        RamBox.Value = defaultRam;
        Ram = defaultRam;

        // GPUs — always allow 0
        GpuBox.Minimum = 0;
        if (gpuOptions.Length > 0)
            GpuBox.Maximum = gpuOptions.Max();
        GpuBox.Value = 0;
        Gpus = 0;

        _syncing = false;
    }

    private int FindNearestPow2Index(int value)
    {
        int bestIdx = 0;
        int bestDiff = int.MaxValue;
        for (int i = 0; i < _ramPower2Values.Length; i++)
        {
            var diff = Math.Abs(_ramPower2Values[i] - value);
            if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
        }
        return bestIdx;
    }

    private void OnCoresBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || double.IsNaN(args.NewValue)) return;
        _syncing = true;
        Cores = (int)args.NewValue;
        CoresSlider.Value = Cores;
        _syncing = false;
    }

    private void OnCoresSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        Cores = (int)e.NewValue;
        CoresBox.Value = Cores;
        _syncing = false;
    }

    private void OnRamBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || double.IsNaN(args.NewValue)) return;
        _syncing = true;
        Ram = (int)args.NewValue;
        RamSlider.Value = FindNearestPow2Index(Ram);
        _syncing = false;
    }

    private void OnRamSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        var idx = (int)e.NewValue;
        if (idx >= 0 && idx < _ramPower2Values.Length)
        {
            Ram = _ramPower2Values[idx];
            RamBox.Value = Ram;
        }
        _syncing = false;
    }

    private void OnGpuBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue)) return;
        Gpus = (int)args.NewValue;
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            // Toggle the TeachingTip associated with this button
            if (btn == CpuHelpBtn) CpuTip.IsOpen = !CpuTip.IsOpen;
            else if (btn == RamHelpBtn) RamTip.IsOpen = !RamTip.IsOpen;
            else if (btn == GpuHelpBtn) GpuTip.IsOpen = !GpuTip.IsOpen;
        }
    }
}
