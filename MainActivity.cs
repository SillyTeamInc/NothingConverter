global using Log = Android.Util.Log;
using System.Diagnostics;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Ffmpegkit.Droid;
#pragma warning disable CA1416

namespace NothingConverter;

public struct ConvertedFile
{
    public string FilePath { get; init; }
    public string RelativePath { get; init; }
    public string DisplayName { get; init; }
}

[Activity(
    Label = "Nothing Encoder",
    MainLauncher = true,
    Theme = "@style/NothingTheme",
    LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(
    new[] { Android.Content.Intent.ActionSend },
    Categories = new[] { Android.Content.Intent.CategoryDefault },
    DataMimeType = "audio/*")]
[IntentFilter(
    new[] { Android.Content.Intent.ActionSend },
    Categories = new[] { Android.Content.Intent.CategoryDefault },
    DataMimeType = "video/*")]
[IntentFilter(
    new[] { Android.Content.Intent.ActionSend },
    Categories = new[] { Android.Content.Intent.CategoryDefault },
    DataMimeType = "image/*")]
public class MainActivity : AppCompatActivity
{
    private const int RequestPickMedia = 1001;

    private List<Android.Net.Uri> _selectedUris = [];
    private List<ConvertedFile> _lastConvertedFiles = [];
    private FormatInfo.OutputFormat _selectedFormat = FormatInfo.All[0];
    private bool _settingsExpanded = false;

    private TextView     _tvAppTitle          = null!;
    private TextView     _tvAppSubtitle       = null!;
    private TextView     _tvFileName          = null!;
    private TextView     _tvFileFormat        = null!;
    private TextView     _tvStatus            = null!;
    private TextView     _tvSelectedFileLabel = null!;
    private TextView     _tvSettingsChevron   = null!;
    private LinearLayout _cardFileInfo        = null!;
    private LinearLayout _layoutSettingsToggle = null!;
    private LinearLayout _layoutSettingsBody  = null!;
    private Spinner      _spinnerFormat       = null!;
    private EditText     _etOutputPath        = null!;
    private Button       _btnSelectFile       = null!;
    private Button       _btnConvert          = null!;
    private LinearLayout _layoutShareView     = null!;
    private TextView     _tvShareButton     = null!;
    private TextView     _tvViewButton      = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        FFmpegKitConfig.EnableRedirection(); 
        FFmpegKitConfig.LogRedirectionStrategy = LogRedirectionStrategy.NeverPrintLogs;
        FFmpegKitConfig.LogLevel = Level.AvLogWarning;

        _selectedUris = [];
        _lastConvertedFiles = [];
        _tvAppTitle           = FindViewById<TextView>(Resource.Id.tvAppTitle)!;
        _tvAppSubtitle        = FindViewById<TextView>(Resource.Id.tvAppSubtitle)!;
        _tvFileName           = FindViewById<TextView>(Resource.Id.tvFileName)!;
        _tvFileFormat         = FindViewById<TextView>(Resource.Id.tvFileFormat)!;
        _tvStatus             = FindViewById<TextView>(Resource.Id.tvStatus)!;
        _tvSelectedFileLabel  = FindViewById<TextView>(Resource.Id.tvSelectedFileLabel)!;
        _tvSettingsChevron    = FindViewById<TextView>(Resource.Id.tvSettingsChevron)!;
        _cardFileInfo         = FindViewById<LinearLayout>(Resource.Id.cardFileInfo)!;
        _layoutSettingsToggle = FindViewById<LinearLayout>(Resource.Id.layoutSettingsToggle)!;
        _layoutSettingsBody   = FindViewById<LinearLayout>(Resource.Id.layoutSettingsBody)!;
        _spinnerFormat        = FindViewById<Spinner>(Resource.Id.spinnerFormat)!;
        _etOutputPath         = FindViewById<EditText>(Resource.Id.etOutputPath)!;
        _btnSelectFile        = FindViewById<Button>(Resource.Id.btnSelectFile)!;
        _btnConvert           = FindViewById<Button>(Resource.Id.btnConvert)!;
        _layoutShareView      = FindViewById<LinearLayout>(Resource.Id.layoutShareView)!;
        _tvShareButton        = FindViewById<TextView>(Resource.Id.tvShareButton)!;
        _tvViewButton         = FindViewById<TextView>(Resource.Id.tvViewButton)!;
        
        _tvShareButton.Click += OnShareClicked;
        _tvShareButton.Clickable = true;
        
        _tvViewButton.Click += OnViewClicked;
        _tvViewButton.Clickable = true;

        SetupFormatSpinner();
        SetupSettingsToggle();

        _btnSelectFile.Click += OnSelectFileClicked;
        _btnConvert.Click += (sender, args) =>
        {
            SetConvertEnabled(false);
            try
            {
                OnConvertClicked(sender, args);
            } catch (Exception ex)
            {
                Log.Error("NothingEncoder", $"Conversion error: {ex}");
                Toast.MakeText(this, "An error occurred during conversion.", ToastLength.Long)?.Show();
            }
        };

        HandleIncomingIntent(Intent);
        
        var prefs = GetSharedPreferences("encoder_prefs", FileCreationMode.Private);
        var savedPath = prefs.GetString("output_path", null);
        if (!string.IsNullOrEmpty(savedPath))
            _etOutputPath.SetText(savedPath, TextView.BufferType.Editable);

        var formatIndex = prefs.GetInt("format_index", 0);
        if (formatIndex >= 0 && formatIndex < FormatInfo.All.Length)        
        {
            _selectedFormat = FormatInfo.All[formatIndex];
            _spinnerFormat.SetSelection(formatIndex);
            _tvAppSubtitle.Text = "→ " + _selectedFormat.Extension.ToUpperInvariant();
        }
        
        CleanupCache();
    }

    private void SetupSettingsToggle()
    {
        _layoutSettingsToggle.Click += (_, _) =>
        {
            _settingsExpanded = !_settingsExpanded;
            _layoutSettingsBody.Visibility = _settingsExpanded ? ViewStates.Visible : ViewStates.Gone;
            _tvSettingsChevron.Text = _settingsExpanded ? "▲" : "▼";
        };
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent != null) HandleIncomingIntent(intent);
    }

    private void HandleIncomingIntent(Intent intent)
    {
        if (intent.Action != Intent.ActionSend) return;
        var uri = intent.GetParcelableExtra(Intent.ExtraStream) as Android.Net.Uri;
        if (uri != null) LoadUris(uri);
    }

    private void SetupFormatSpinner()
    {
        var labels = FormatInfo.All
            .Select(f => $"{f.Extension.ToUpperInvariant()}  [{f.Category}]")
            .ToArray();

        var adapter = new ArrayAdapter<string>(this,
            Resource.Layout.spinner_item, labels);
        adapter.SetDropDownViewResource(Resource.Layout.spinner_dropdown_item);
        _spinnerFormat.Adapter = adapter;

        _spinnerFormat.ItemSelected += (_, e) =>
        {
            _selectedFormat = FormatInfo.All[e.Position];
            _tvAppSubtitle.Text = "→ " + _selectedFormat.Extension.ToUpperInvariant();
            var prefs = GetSharedPreferences("encoder_prefs", FileCreationMode.Private);
            prefs.Edit()!.PutInt("format_index", e.Position).Apply();
        };
    }

    private void OnSelectFileClicked(object? sender, EventArgs e)
    {
        _tvAppTitle.Text = "ENCODER_";
        _tvStatus.Text = "NO FILES LOADED";
        _cardFileInfo.Visibility = ViewStates.Gone;
        _tvSelectedFileLabel.Visibility = ViewStates.Gone;
        _layoutShareView.Visibility = ViewStates.Gone;
        _selectedUris.Clear();
        _lastConvertedFiles.Clear();
        
        var pick = new Intent(Intent.ActionGetContent);
        pick.SetType("*/*");
        pick.PutExtra(Intent.ExtraMimeTypes, new[] { "audio/*", "video/*", "image/*" });
        pick.AddCategory(Intent.CategoryOpenable);
        pick.PutExtra(Intent.ExtraAllowMultiple, true);
        StartActivityForResult(Intent.CreateChooser(pick, "Select file"), RequestPickMedia);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        Log.Info("NothingEncoder", $"OnActivityResult: requestCode={requestCode}, resultCode={resultCode}, data={data}");
        Log.Info("NothingEncoder", $"Data URI: {data?.Data}");
        if (requestCode != RequestPickMedia || resultCode != Result.Ok ||
            (data?.Data == null && data?.ClipData == null)) return;
        
        if (data.ClipData != null)
        {
            var clipData = data.ClipData!;
            var uris = new Android.Net.Uri[clipData.ItemCount];
            for (int i = 0; i < clipData.ItemCount; i++)                
            {
                uris[i] = clipData.GetItemAt(i)?.Uri!;
                Log.Info("NothingEncoder", $"ClipData URI[{i}]: {uris[i]}");
            }
            LoadUris(uris);
            return;
        }
            
        Log.Info("NothingEncoder", $"Received URI from picker: {data.Data}");
        LoadUris(data.Data);
    }

    private void LoadUris(params Android.Net.Uri[] uris)
    {
        if (uris.Length == 0)
        {
            // idk if its even possible to hit this conditional
            SetStatus("NO FILES LOADED??");
            return;
        }
        
        _selectedUris.Clear();
        _selectedUris.AddRange(uris);
        
        if (_selectedUris.Count > 1)
        {
            var firstType = ContentResolver.GetType(_selectedUris[0])?.Split('/')[0];
            if (firstType == null)
            {
                SetStatus("FAILED TO DETERMINE FILE TYPE.");
                SetConvertEnabled(true);
                SetSelectEnabled(true);
                return;
            }

            foreach (var uri in _selectedUris)
            {
                var type = ContentResolver.GetType(uri)?.Split('/')[0];
                if (type != firstType)
                {
                    SetStatus("ALL FILES MUST BE OF THE SAME TYPE.");
                    _selectedUris.Clear();
                    SetConvertEnabled(true);
                    SetSelectEnabled(true);
                    return;
                }
            }
        }
        
        if (uris.Length == 1) ShowSelectedFileInfo(uris[0]);
        else ShowMultipleFilesInfo(uris.Length);

        
        SetStatus("READY TO CONVERT");
        SetConvertEnabled(true);
    }
    
    private void ShowSelectedFileInfo(Android.Net.Uri uri)
    {
        var displayName = ConverterService.GetFileNameFromUri(this, uri);
        var ext = Path.GetExtension(displayName).ToUpperInvariant().TrimStart('.');
        
        _tvFileName.Text   = displayName;
        _tvFileFormat.Visibility = ViewStates.Visible;
        _tvFileFormat.Text = string.IsNullOrEmpty(ext) ? "" : $"[ {ext} ]";
        _cardFileInfo.Visibility        = ViewStates.Visible;
        _tvSelectedFileLabel.Visibility = ViewStates.Visible;
    }
    
    public void ShowMultipleFilesInfo(int count)
    {
        _tvFileName.Text = $"{count} files selected";
        _tvFileFormat.Visibility = ViewStates.Gone;
        _cardFileInfo.Visibility = ViewStates.Visible;
        _tvSelectedFileLabel.Visibility = ViewStates.Visible;
    }
    
    private void CleanupCache()
    {
        try
        {
            var cacheDir = this.CacheDir!.AbsolutePath;
            var directory = new DirectoryInfo(cacheDir);
        
            foreach (var file in directory.GetFiles())
            {
                if (file.LastWriteTime < DateTime.Now.AddMinutes(-10))
                {
                    file.Delete();
                    Log.Info("NothingEncoder", $"Deleted old cache file: {file.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("NothingEncoder", $"Cleanup failed: {ex.Message}");
        }
    }

    private async void OnConvertClicked(object? sender, EventArgs e)
    {
        if (_selectedUris.Count == 0)
        {
            SetStatus("NO FILES TO CONVERT.");
            return;
        }

        SetConvertEnabled(false);
        SetSelectEnabled(false);
        SetStatus("STARTING CONVERSION...");
        
        List<ConvertedFile> convertedFiles = [];
        int index = 0;
        if (_selectedUris.Count > 1) _tvAppTitle.Text = $"ENCODER_ {index}/{_selectedUris.Count}";
        foreach (var uri in _selectedUris)
        {
            index++;
            var processedCount = index;
            if (_selectedUris.Count > 1)
                _tvAppTitle.Post(() =>
                {
                    _tvAppTitle.Text = $"ENCODER_ {processedCount}/{_selectedUris.Count}";
                });
            
            var result = await DoConvert(uri);
            if (result != null) convertedFiles.Add((ConvertedFile)result);
        }
        
        _tvAppTitle.Text = "ENCODER_ ✓";
        _layoutShareView.Visibility = ViewStates.Visible;
        _lastConvertedFiles = convertedFiles;
        
        
        try
        {
            var vibrator = (Vibrator?)GetSystemService(VibratorService);
            vibrator?.Vibrate(VibrationEffect.CreateWaveform(new long[] { 0, 30, 60, 30 }, -1));
        }
        catch { }
        
        if (convertedFiles.Count > 1)
        {
            SetStatus($"SAVED: {convertedFiles.Count} files");
        }
    }

    private async Task<ConvertedFile?> DoConvert(Android.Net.Uri uri)
    {
        var customPath = _etOutputPath.Text?.Trim();

        var prefs = GetSharedPreferences("encoder_prefs", FileCreationMode.Private);
        prefs.Edit()!.PutString("output_path", customPath ?? "").Apply();

        SetConvertEnabled(false);
        SetSelectEnabled(false);
        SetStatus("COPYING FILE...");

        string? tempInputPath = null;
        try
        {
            tempInputPath = await Task.Run(() =>
                ConverterService.CopyUriToCache(this, uri)).ConfigureAwait(false);

            var displayName = await Task.Run(() => ConverterService.GetFileNameFromUri(this, uri));
            var baseName    = Path.GetFileNameWithoutExtension(displayName);
            var progress = new Progress<string>(SetStatus);

            var savedName = await ConverterService.ConvertAndSaveAsync(
                this, tempInputPath, baseName, _selectedFormat,
                customOutputPath: string.IsNullOrWhiteSpace(customPath) ? null : customPath,
                progress: progress).ConfigureAwait(false);
            
    
            SetStatus($"SAVED: {displayName}");
           
            return new ConvertedFile
            {
                FilePath = ConverterService.LastSavedFile,
                RelativePath = ConverterService.LastSavedRelativePath,
                DisplayName = savedName
            };
        }
        catch (Exception ex)
        {
            SetStatus($"ERROR: {ex.Message}");
            Toast.MakeText(this, "Conversion failed.", ToastLength.Long)?.Show();
            SetConvertEnabled(true);
        }
        finally
        {
            SetSelectEnabled(true);
            if (tempInputPath != null && File.Exists(tempInputPath))
                try { File.Delete(tempInputPath); } catch { }
        }
        
        return null;
    }

    
    private void OnShareClicked(object? sender, EventArgs e)
    {
        if (_lastConvertedFiles.Count == 0) return;
        try
        {
            var uris = _lastConvertedFiles
                .Select(f => Android.Net.Uri.Parse(f.FilePath))
                .ToList();

            var mimes = _lastConvertedFiles
                .Select(f => FormatInfo.GetMimeTypeForExtension(
                    Path.GetExtension(f.DisplayName).TrimStart('.')) ?? "*/*")
                .Distinct()
                .ToList();
            var mime = mimes.Count == 1 ? mimes[0] : "*/*";

            var shareIntent = new Intent(
                uris.Count == 1 ? Intent.ActionSend : Intent.ActionSendMultiple);
            shareIntent.SetType(mime);
            shareIntent.AddFlags(ActivityFlags.GrantReadUriPermission);

            if (uris.Count == 1)
            {
                shareIntent.PutExtra(Intent.ExtraStream, uris[0]);
            }
            else
            {
                var parcelableList = uris
                    .Cast<IParcelable>()
                    .ToList();
                shareIntent.PutParcelableArrayListExtra(Intent.ExtraStream, parcelableList);
            }

            var title = _lastConvertedFiles.Count == 1
                ? _lastConvertedFiles[0].DisplayName
                : $"{_lastConvertedFiles.Count} files";

            StartActivity(Intent.CreateChooser(shareIntent, title));
        }
        catch (Exception ex)
        {
            Log.Error("NothingEncoder", $"Share error: {ex.Message}");
        }
    }
    
    private void OnViewClicked(object? sender, EventArgs e)
    {
        if (_lastConvertedFiles.Count == 0) return;
        string lastSavedRelativePath = _lastConvertedFiles[0].RelativePath;

        if (Debugger.IsAttached)
        {
            // This doesn't work on debug mode for some reason and
            // i want to prevent it from deadlocking the while im testing
            Toast.MakeText(this, $"View folder: {lastSavedRelativePath}\n(Viewing doesn't currently\nwork with a debugger attached)", ToastLength.Long)?.Show();
            return;
        }
        

        Task.Run(() =>
        {
            try
            {
                var encoded = Uri.EscapeDataString("primary:" + lastSavedRelativePath);
                var folderUri = Android.Net.Uri.Parse(
                    $"content://com.android.externalstorage.documents/document/{encoded}");

                var intent = new Intent(Intent.ActionView);
                intent.SetDataAndType(folderUri, "vnd.android.document/directory");
                intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);

                RunOnUiThread(() =>
                {
                    try { StartActivity(intent); }
                    catch
                    {
                        Toast.MakeText(this, $"Saved to: {lastSavedRelativePath}", ToastLength.Long)?.Show();
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error("NothingEncoder", $"View folder error: {ex.Message}");
            }
        });
    }

    private bool IsMainLooper()
    {
        return Looper.MyLooper()?.Equals(Looper.MainLooper) == true;
    }

    private void SetStatus(string msg)
    {
        if (IsMainLooper())
            _tvStatus.Text = msg;
        else
            _tvStatus.Post(() => _tvStatus.Text = msg);
    }
    
    private void SetSelectEnabled(bool enabled)
    {
        if (IsMainLooper())
        {
            _btnSelectFile.Enabled = enabled;
            _btnSelectFile.Alpha   = enabled ? 1f : 0.4f;
        }
        else
            _btnSelectFile.Post(() =>
            {
                _btnSelectFile.Enabled = enabled;
                _btnSelectFile.Alpha   = enabled ? 1f : 0.4f;
            });
    }

    private void SetConvertEnabled(bool enabled)
    {
        if (IsMainLooper())
        {
            _btnConvert.Enabled = enabled;
            _btnConvert.Alpha   = enabled ? 1f : 0.4f;
        }
        else
            _btnConvert.Post(() =>
            {
                _btnConvert.Enabled = enabled;
                _btnConvert.Alpha   = enabled ? 1f : 0.4f;
            });
    }
}