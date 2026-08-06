using Kirana.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;

namespace Kirana.App.Views;

/// <summary>
/// Bulk product import (PRD §51 data tools). Two explicit steps — choose a file to see a preview,
/// then press Import — so nothing is written until the operator has seen exactly what would change.
/// </summary>
public sealed partial class ProductImportDialog : ContentDialog
{
    public ProductImportViewModel ViewModel { get; }

    /// <summary>True once rows were actually written, so the caller knows to refresh its list.</summary>
    public bool ImportedAnything { get; private set; }

    public ProductImportDialog(ProductImportViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnChooseFileClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".xlsx");

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await LoadFileAsync(file);
    }

    private async void OnDownloadTemplateClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = "kirana-product-import-template";
        picker.FileTypeChoices.Add("CSV (comma-separated)", [".csv"]);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await FileIO.WriteTextAsync(file, ViewModel.BuildTemplate());
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Keep the dialog open regardless of outcome — the operator needs to see the success/error
        // message inline, same as every other action in this dialog. A ContentDialog closes itself
        // on a button click unless told not to, hence the deferral.
        var deferral = args.GetDeferral();
        try
        {
            args.Cancel = true;
            await ViewModel.CommitAsync();

            if (ViewModel.IsCommitted)
            {
                ImportedAnything = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnFixRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ProductImportRowViewModel row)
        {
            row.BeginEdit();
        }
    }

    private void OnCancelFixClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ProductImportRowViewModel row)
        {
            row.IsEditing = false;
        }
    }

    private async void OnSaveFixClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ProductImportRowViewModel row)
        {
            return;
        }

        // ApplyPreview (inside ReviseRowAsync) rebuilds ViewModel.Rows from scratch, so this row's
        // own IsEditing/EditFields are discarded along with it — no explicit collapse needed here.
        await ViewModel.ReviseRowAsync(row.RowNumber, row.BuildUpdatedFields());
    }

    /// <summary>Enter inside a Fix-row field must not submit the whole dialog. DefaultButton is
    /// "Primary" (Import) so the highlighted button doubles as the Enter target, which is exactly
    /// right for the common "load file, hit Enter" flow — but it means an un-guarded Enter while
    /// correcting a field here would fire a real import mid-edit, before the row's own Save runs.</summary>
    private void OnFixFieldKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
        }
    }

    private async void OnImportAsNewClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ProductImportRowViewModel row)
        {
            await ViewModel.ImportAsNewAsync(row.RowNumber);
        }
    }

    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ProductImportRowViewModel row)
        {
            ViewModel.RemoveRow(row.RowNumber);
        }
    }

    private void OnUndoRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ProductImportRowViewModel row)
        {
            ViewModel.UndoRemoveRow(row.RowNumber);
        }
    }

    private void OnCloseIconClick(object sender, RoutedEventArgs e) => Hide();

    private void OnViewProductsClick(object sender, RoutedEventArgs e) => Hide();

    private void OnImportAnotherFileClick(object sender, RoutedEventArgs e) => ViewModel.ResetForAnotherFile();

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var file = (await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().FirstOrDefault();
        if (file is null)
        {
            return;
        }

        if (!string.Equals(file.FileType, ".csv", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(file.FileType, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.ShowFileTypeError();
            return;
        }

        await LoadFileAsync(file);
    }

    private async Task LoadFileAsync(StorageFile file)
    {
        // Copy into memory first: the Application layer takes a plain Stream and must not depend on
        // WinRT storage types, and the parser may need to seek.
        using var fileStream = await file.OpenStreamForReadAsync();
        using var stream = new MemoryStream();
        await fileStream.CopyToAsync(stream);
        stream.Position = 0;

        var properties = await file.GetBasicPropertiesAsync();
        await ViewModel.LoadPreviewAsync(stream, file.Name, (long)properties.Size);
    }
}
