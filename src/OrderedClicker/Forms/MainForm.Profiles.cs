using System.Diagnostics;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
    private void CaptureProfileSelectorTextBeforeKeyboardSelection(KeyEventArgs eventArgs)
    {
        if (_profileSelector.DroppedDown)
        {
            return;
        }

        if (eventArgs.KeyCode is Keys.Up
            or Keys.Down
            or Keys.Home
            or Keys.End
            or Keys.PageUp
            or Keys.PageDown)
        {
            _profileSelectorTextBeforeSelection = _profileSelector.Text;
        }
    }

    private void RefreshProfileDirectory()
    {
        var preservedText = _profileSelector.Text;
        try
        {
            var entries = _profileService.ListLocalProfiles();
            _suppressProfileSelection = true;
            _profileSelector.BeginUpdate();
            _profileSelector.Items.Clear();
            _profileSelector.Items.AddRange(entries.Cast<object>().ToArray());
            _profileSelector.SelectedIndex = -1;
            _profileSelector.Text = preservedText;
        }
        catch (Exception exception)
        {
            RecordDiagnostic("profile.list", exception);
            ShowError($"读取方案目录失败：{exception.Message}");
        }
        finally
        {
            _profileSelector.EndUpdate();
            _suppressProfileSelection = false;
        }
    }

    private void SelectLocalProfile()
    {
        if (_suppressProfileSelection
            || _profileSelector.SelectedItem is not LocalProfileEntry selected)
        {
            return;
        }

        var previousText = _profileSelectorTextBeforeSelection
                           ?? _baselineProfile?.Name
                           ?? "默认方案";
        RestoreProfileSelectorText(previousText);

        if (_currentProfilePath is not null
            && ProfileService.PathsEqual(_currentProfilePath, selected.Path))
        {
            return;
        }

        if (!ConfirmSaveBeforeProfileSwitch())
        {
            RestoreProfileSelectorText(previousText);
            SetStatus("已取消切换方案。");
            return;
        }

        var result = _profileService.Import(selected.Path);
        if (!result.Success)
        {
            RestoreProfileSelectorText(previousText);
            ShowError(result.ErrorMessage ?? "本机方案加载失败。");
            return;
        }

        ApplyLocalProfile(result.Profile!, selected.Path);
        ClearExecutionCheckpoint();
        SetStatus($"已打开本机方案：{selected.Path}");
    }

    private bool ConfirmSaveBeforeProfileSwitch()
    {
        if (!HasUnsavedProfileChanges())
        {
            return true;
        }

        var choice = MessageBox.Show(
            this,
            "当前方案有未保存修改。切换前是否保存？\n\n"
            + "选择“是”保存后切换；选择“否”直接切换；选择“取消”留在当前方案。",
            "切换方案",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        return choice switch
        {
            DialogResult.Yes => SaveProfile(),
            DialogResult.No => true,
            _ => false
        };
    }

    private bool HasUnsavedProfileChanges()
    {
        if (_saveImportedProfileAsCopy || _baselineProfile is null)
        {
            return true;
        }

        return !ProfileService.ProfilesEqual(
            _baselineProfile,
            CreateProfileSnapshot());
    }

    private void ApplyLocalProfile(ClickProfile profile, string sourcePath)
    {
        ApplyProfile(profile);
        _currentProfilePath = Path.GetFullPath(sourcePath);
        _currentProfileFileHash = File.Exists(sourcePath)
            ? ProfileService.ComputeFileSha256(sourcePath)
            : null;
        _saveImportedProfileAsCopy = false;
        _importedProfileSourcePath = null;
        UpdateProfileBaseline();
        _lastDraftSnapshot = ProfileService.CloneProfile(profile);
        _draftService?.Discard();
    }

    private void UpdateProfileBaseline(ClickProfile? profile = null)
    {
        _baselineProfile = ProfileService.CloneProfile(
            profile ?? CreateProfileSnapshot());
    }

    private void RestoreProfileSelectorText(string text)
    {
        _suppressProfileSelection = true;
        _profileSelector.SelectedIndex = -1;
        _profileSelector.Text = text;
        _suppressProfileSelection = false;
    }

    private void OpenProfilesDirectory()
    {
        try
        {
            Directory.CreateDirectory(_profileService.ProfilesDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _profileService.ProfilesDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            RecordDiagnostic("profile.directory.open", exception);
            ShowError($"打开方案目录失败：{exception.Message}");
        }
    }
}
