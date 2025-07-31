using AmaScan.sqliteModels;
using Data.Model;
using SQLite;
using Microsoft.Maui.Dispatching;
using AmaScan.Data;

namespace AmaScan.Classes;

public static class SoMergeHelper
{
    /// <summary>
    /// Handles the complete SO fetch and merge workflow
    /// </summary>
    public static async Task<bool> HandleSoFetchAndMergeAsync(
        string soNumber,
        SalesOrderResponse freshData,
        Label customerLabel,
        Label dueDateLabel,
        Frame soHeaderFrame,
        CollectionView soLinesView,
        Button loadSOButton,
        string workflowName)
    {
        try
        {
            var databaseHelper = AmaScanDatabase.GetDatabaseHelper();

            // Run database operations on background thread
            var result = await Task.Run(async () =>
            {
                // Step 1: Check local database first
                var existingSo = await databaseHelper.GetSoHeaderByOrderNoAsync(soNumber);
                return existingSo;
            });

            var existingSo = result;

            // Step 2: If SO exists locally, merge fresh data with existing workflow data
            if (existingSo != null)
            {
                await MergeFreshDataWithExistingAsync(soNumber, freshData, databaseHelper,
                    customerLabel, dueDateLabel, soHeaderFrame, soLinesView, loadSOButton, workflowName);
            }
            else
            {
                // Show fresh data from API on main thread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    customerLabel.Text = $"Customer: {freshData.CustomerName}";
                    dueDateLabel.Text = $"Due Date: {freshData.DueDate:yyyy-MM-dd}";
                    soHeaderFrame.IsVisible = true;

                    soLinesView.ItemsSource = freshData.Lines;
                    soLinesView.IsVisible = true;
                    loadSOButton.IsVisible = true;
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Error", $"Could not load SO: {soNumber} : Message:- {ex.Message}", "OK");
            return false;
        }
    }

    /// <summary>
    /// Loads existing SO data from database for display
    /// </summary>
    public static async Task LoadExistingSoForDisplayAsync(
        string soNumber,
        DatabaseHelper databaseHelper,
        Label customerLabel,
        Label dueDateLabel,
        Frame soHeaderFrame,
        CollectionView soLinesView,
        Button loadSOButton)
    {
        try
        {
            // Run database operations on background thread
            var result = await Task.Run(async () =>
            {
                // Load header and lines in parallel for better performance
                var headerTask = databaseHelper.GetSoHeaderByOrderNoAsync(soNumber);
                var linesTask = databaseHelper.GetSoLinesByOrderNoAsync(soNumber);

                await Task.WhenAll(headerTask, linesTask);

                var existingSo = await headerTask;
                var existingLines = await linesTask;

                return new { existingSo, existingLines };
            });

            var existingSo = result.existingSo;
            var existingLines = result.existingLines;

            if (existingSo == null || !existingLines.Any()) return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                customerLabel.Text = $"Customer: {existingSo.CustomerName ?? "N/A"}";
                dueDateLabel.Text = $"Due Date: {existingSo.DueDate:yyyy-MM-dd}";
                soHeaderFrame.IsVisible = true;
                soLinesView.ItemsSource = existingLines;
                soLinesView.IsVisible = true;
                loadSOButton.IsVisible = true;
            });

            // Set PickingWorkflowSession.CurrentSoHeader after loading
            PickingWorkflowSession.CurrentSoHeader = existingSo;
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Error", $"Failed to load existing SO: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Merges fresh SO data with existing workflow data
    /// </summary>
    public static async Task MergeFreshDataWithExistingAsync(
        string soNumber,
        SalesOrderResponse freshData,
        DatabaseHelper databaseHelper,
        Label customerLabel,
        Label dueDateLabel,
        Frame soHeaderFrame,
        CollectionView soLinesView,
        Button loadSOButton,
        string workflowName)
    {
        try
        {
            // Run database operations on background thread
            var mergeResult = await Task.Run(async () =>
            {
                // Get existing lines before merge for comparison
                var existingLinesBefore = await databaseHelper.GetSoLinesByOrderNoAsync(soNumber);
                var existingLineKeys = existingLinesBefore
                    .Select(l => $"{l.ItemCode}_{l.ItemBarcode}")
                    .ToHashSet();
                var freshLineKeys = freshData.Lines
                    .Select(l => $"{l.ItemCode}_{l.ItemBarcode}")
                    .ToHashSet();

                // Merge fresh data with existing workflow data
                await databaseHelper.MergeSoDataAsync(freshData);

                // Get lines after merge for comparison
                var existingLinesAfter = await databaseHelper.GetSoLinesByOrderNoAsync(soNumber);

                // Calculate merge summary using composite keys
                var newLines = freshLineKeys.Except(existingLineKeys).Count();
                var removedLines = existingLineKeys.Except(freshLineKeys).Count();
                var updatedLines = existingLinesBefore.Count - removedLines;

                return new { newLines, removedLines, updatedLines };
            });

            // Show merge summary on main thread
            var summary = $"Merge Complete!\n\n" +
                         $"• {mergeResult.updatedLines} lines updated\n" +
                         $"• {mergeResult.newLines} new lines added\n" +
                         $"• {mergeResult.removedLines} lines removed\n\n" +
                         $"Your {workflowName.ToLower()} progress has been preserved.";

            if (mergeResult.removedLines > 0)
            {
                await Application.Current.MainPage.DisplayAlert("Merge Summary", summary, "OK");
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("Merge Summary", summary, "OK");
            }

            // *** Refresh the UI after the alert ***
            await LoadExistingSoForDisplayAsync(soNumber, databaseHelper, customerLabel, dueDateLabel, soHeaderFrame, soLinesView, loadSOButton);
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Merge Error", $"Failed to merge fresh data: {ex.Message}", "OK");
        }
    }
}