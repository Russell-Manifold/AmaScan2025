using AmaScan.Controls;
using Microsoft.Maui.Handlers;

#if ANDROID
using Android.Views.InputMethods;
#endif

namespace AmaScan
{
    public static class EntryHandlerMapper
    {
        public static void Configure()
        {
#if ANDROID
            EntryHandler.Mapper.AppendToMapping(nameof(NoKeyboardEntry), (handler, view) =>
            {
                if (view is NoKeyboardEntry)
                {
                    handler.PlatformView.ShowSoftInputOnFocus = false;
                }
            });
#endif
        }
    }
}