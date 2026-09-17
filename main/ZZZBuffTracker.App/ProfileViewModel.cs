using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.App;

public sealed class BuffEditor
{
    public string Id { get; set; } = "new-buff";
    public string Name { get; set; } = "Buff mới";
    public double DurationSeconds { get; set; } = 6;
    public int MaxStacks { get; set; } = 1;
    public RetriggerMode Retrigger { get; set; }
    public SignalLossPolicy SignalLoss { get; set; }
    public bool StackRefreshesDuration { get; set; } = true;
    public string? DriveDiscSetId { get; set; }
    public BuffDefinition ToDefinition() => new(Id.Trim(), Name.Trim(), TimeSpan.FromSeconds(DurationSeconds), MaxStacks,
        Retrigger, SignalLoss, StackRefreshesDuration, string.IsNullOrWhiteSpace(DriveDiscSetId) ? null : DriveDiscSetId.Trim());
    public static BuffEditor From(BuffDefinition d) => new()
    { Id = d.Id, Name = d.Name, DurationSeconds = d.Duration.TotalSeconds, MaxStacks = d.MaxStacks,
        Retrigger = d.Retrigger, SignalLoss = d.SignalLoss, StackRefreshesDuration = d.StackRefreshesDuration, DriveDiscSetId = d.DriveDiscSetId };
}

public sealed class ProfileViewModel : ObservableModel
{
    private readonly ProfileService profiles;
    private readonly TrackingService tracking;
    private readonly OverlayViewModel overlay;
    private CharacterPreset? selected;
    private ImmutableArray<BuffRule> editingRules;
    private string characterId = "", characterName = "", message = "";
    private string? editingId;
    private long editingRevision;
    private ProfileDocument? pendingImport;
    private long importRevision;
    public ObservableCollection<CharacterPreset> Presets { get; } = [];
    public ObservableCollection<BuffEditor> Buffs { get; } = [];
    public BuffEditor? SelectedBuff { get; set; }
    public Array RetriggerModes { get; } = Enum.GetValues<RetriggerMode>();
    public Array LossPolicies { get; } = Enum.GetValues<SignalLossPolicy>();
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand AddBuffCommand { get; }
    public ICommand RemoveBuffCommand { get; }
    public ICommand SimulateCharacterCommand { get; }

    public ProfileViewModel(ProfileService profiles, TrackingService tracking, OverlayViewModel overlay)
    {
        this.profiles = profiles; this.tracking = tracking; this.overlay = overlay;
        NewCommand = Command(() => { New(); return Task.CompletedTask; });
        SaveCommand = Command(SaveAsync);
        ReloadCommand = Command(ReloadAsync);
        ApplyCommand = Command(() => tracking.SelectCharacterAsync(CharacterId.Trim()));
        AddBuffCommand = Command(() => { Buffs.Add(new() { Id = "buff-" + Guid.NewGuid().ToString("N")[..8] }); return Task.CompletedTask; });
        RemoveBuffCommand = Command(() => { if (SelectedBuff is { } buff) Buffs.Remove(buff); return Task.CompletedTask; });
        SimulateCharacterCommand = Command(() =>
        {
            Message = tracking.SimulateCharacterChanged(CharacterId.Trim()) ? "Đã gửi CharacterChanged giả lập." : "Hãy Start tracking trước.";
            return Task.CompletedTask;
        });
    }
    private ICommand Command(Func<Task> action) => new AsyncCommand(action, ReportError);
    public CharacterPreset? SelectedPreset
    {
        get => selected;
        set
        {
            selected = value; Changed();
            if (value is null) return;
            editingId = value.CharacterId; editingRevision = profiles.Current.Revision;
            CharacterId = value.CharacterId;
            CharacterName = profiles.Current.Characters.First(c => c.Id == value.CharacterId).Name;
            editingRules = value.Rules;
            Buffs.Clear(); foreach (var buff in value.Buffs) Buffs.Add(BuffEditor.From(buff));
            SelectedBuff = Buffs.FirstOrDefault(); Changed(nameof(SelectedBuff));
        }
    }
    public string CharacterId { get => characterId; set { characterId = value; Changed(); } }
    public string CharacterName { get => characterName; set { characterName = value; Changed(); } }
    public string Message { get => message; private set { message = value; Changed(); } }
    public string ImportSummary => pendingImport is null ? "Chưa có file import đã kiểm tra."
        : $"Thay thế profile hiện tại bằng {pendingImport.Presets.Length} preset, {pendingImport.Characters.Length} nhân vật, {pendingImport.DriveDiscSets.Length} bộ đĩa: "
            + string.Join(", ", pendingImport.Presets.Select(p => $"{p.CharacterId} ({p.Buffs.Length} buff)"))
            + ". Có backup; cần bấm Áp dụng import.";
    public void ReportError(Exception ex) => Message = "Lỗi: " + ex.Message;
    public async Task InitializeAsync()
    {
        try { await ReloadAsync(); } catch (Exception ex) { ReportError(ex); }
    }
    public async Task ReloadAsync()
    {
        await profiles.ReloadAsync(); RefreshList(); Message = "Đã nạp profile. Chọn preset để sửa hoặc tạo mới.";
    }
    private void RefreshList(string? id = null)
    {
        Presets.Clear(); foreach (var p in profiles.Current.Presets) Presets.Add(p);
        SelectedPreset = Presets.FirstOrDefault(p => p.CharacterId == id) ?? Presets.FirstOrDefault();
        if (SelectedPreset is null) New();
    }
    private void New()
    {
        selected = null; Changed(nameof(SelectedPreset)); editingId = null; editingRevision = profiles.Current.Revision;
        CharacterId = ""; CharacterName = ""; editingRules = default;
        Buffs.Clear(); Buffs.Add(new()); Message = "Preset mới. Nhập ID nhân vật khác preset đã có.";
    }
    private async Task SaveAsync()
    {
        var id = CharacterId.Trim();
        if (editingId is not null && id != editingId) throw new ArgumentException("Để đổi ID nhân vật, hãy tạo preset mới.");
        if (editingId is null && profiles.Current.Presets.Any(p => p.CharacterId == id))
            throw new ArgumentException("ID đã tồn tại. Chọn preset đó để sửa; không ghi đè preset mới.");
        var definitions = Buffs.Select(b => b.ToDefinition()).ToImmutableArray();
        // Preserve imported custom rules. New buffs get defaults; removed buffs lose their rules.
        var rules = editingRules;
        if (!rules.IsDefault)
        {
            var previousIds = selected?.Buffs.Select(b => b.Id).ToHashSet() ?? [];
            rules = rules.Where(r => definitions.Any(b => b.Id == r.BuffId)).ToImmutableArray();
            var added = definitions.Where(b => !previousIds.Contains(b.Id)).ToImmutableArray();
            rules = rules.AddRange(PresetRules.Resolve(new(id, 1, added)));
        }
        await profiles.SavePresetAsync(new(id, CharacterName.Trim()), new(id, 1, definitions, rules), editingRevision);
        RefreshList(id);
        if (tracking.Snapshot.Status != TrackingStatus.Stopped && tracking.Snapshot.CharacterId == id)
            await tracking.SelectCharacterAsync(id);
        Message = "Đã lưu preset; version tăng và state đang chạy được reset nếu cần.";
    }
    public async Task DeleteSelectedAsync()
    {
        var id = editingId ?? throw new InvalidOperationException("Chọn preset đã lưu để xóa.");
        await profiles.DeletePresetAsync(id, editingRevision);
        if (tracking.Snapshot.Status != TrackingStatus.Stopped && tracking.Snapshot.CharacterId == id)
            await tracking.SelectCharacterAsync(id);
        RefreshList(); Message = "Đã xóa preset; metadata nhân vật được giữ lại.";
    }
    public async Task PreviewImportAsync(string path)
    {
        pendingImport = null; Changed(nameof(ImportSummary));
        pendingImport = await profiles.PreviewImportAsync(path); importRevision = profiles.Current.Revision;
        Changed(nameof(ImportSummary)); Message = "File hợp lệ. Kiểm tra số lượng trước khi áp dụng.";
    }
    public async Task ApplyImportAsync()
    {
        var preview = pendingImport ?? throw new InvalidOperationException("Chọn file import trước.");
        preview = preview with { Overlay = preview.Overlay with { Geometry = overlay.ExportSettings.Geometry } };
        await profiles.ApplyImportAsync(preview, importRevision);
        pendingImport = null; Changed(nameof(ImportSummary));
        overlay.ApplySettings(preview.Overlay);
        RefreshList();
        if (tracking.Snapshot.Status != TrackingStatus.Stopped && tracking.Snapshot.CharacterId is { } active)
            await tracking.SelectCharacterAsync(active);
        Message = "Đã import và backup profile cũ. Cấu hình hình ảnh đã áp dụng; vị trí màn hình hiện tại được giữ.";
    }
    public Task ExportAsync(string path, bool overwrite) => profiles.ExportAsync(path, overlay.ExportSettings, overwrite);
}
