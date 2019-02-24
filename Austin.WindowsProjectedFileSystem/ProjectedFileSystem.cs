using System;
using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    public class ProjectedFileSystem : IDisposable
    {
        readonly Guid mUniqueId;
        readonly IProjectedFileSystemCallbacks mCallbacks;
        readonly Interop.ProjFs.PRJ_CALLBACKS mNativeCallbacksDelegates;
        readonly Interop.ProjFs.PRJ_CALLBACKS_INTPTR mNativeCallbacksIntptr;
        readonly Interop.ProjFs.PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT mContext;

        GCHandle mCallbacksPin;

        public ProjectedFileSystem(string path, IProjectedFileSystemCallbacks callbacks)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (callbacks == null)
                throw new ArgumentNullException(nameof(callbacks));

            mCallbacks = callbacks;
            mUniqueId = Guid.NewGuid();

            int hr;
            hr = Interop.ProjFs.PrjMarkDirectoryAsPlaceholder(path, null, IntPtr.Zero, mUniqueId);
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);

            mNativeCallbacksDelegates = new Interop.ProjFs.PRJ_CALLBACKS()
            {
                StartDirectoryEnumerationCallback = new Interop.ProjFs.PRJ_START_DIRECTORY_ENUMERATION_CB(StartDirectoryEnumerationCallback),
                EndDirectoryEnumerationCallback = new Interop.ProjFs.PRJ_END_DIRECTORY_ENUMERATION_CB(EndDirectoryEnumerationCallback),
                GetDirectoryEnumerationCallback = new Interop.ProjFs.PRJ_GET_DIRECTORY_ENUMERATION_CB(GetDirectoryEnumerationCallback),
                GetPlaceholderInfoCallback = new Interop.ProjFs.PRJ_GET_PLACEHOLDER_INFO_CB(GetPlaceholderInfoCallback),
                GetFileDataCallback = new Interop.ProjFs.PRJ_GET_FILE_DATA_CB(GetFileDataCallback),
                QueryFileNameCallback = new Interop.ProjFs.PRJ_QUERY_FILE_NAME_CB(QueryFileNameCallback),
                NotificationCallback = new Interop.ProjFs.PRJ_NOTIFICATION_CB(NotificationCallback),
                CancelCommandCallback = new Interop.ProjFs.PRJ_CANCEL_COMMAND_CB(CancelCommandCallback),
            };
            mNativeCallbacksIntptr = new Interop.ProjFs.PRJ_CALLBACKS_INTPTR()
            {
                StartDirectoryEnumerationCallback = Marshal.GetFunctionPointerForDelegate(mNativeCallbacksDelegates.StartDirectoryEnumerationCallback),
                EndDirectoryEnumerationCallback = Marshal.GetFunctionPointerForDelegate(mNativeCallbacksDelegates.EndDirectoryEnumerationCallback),
                GetDirectoryEnumerationCallback = Marshal.GetFunctionPointerForDelegate(mNativeCallbacksDelegates.GetDirectoryEnumerationCallback),
                GetPlaceholderInfoCallback = Marshal.GetFunctionPointerForDelegate(mNativeCallbacksDelegates.GetPlaceholderInfoCallback),
                GetFileDataCallback = Marshal.GetFunctionPointerForDelegate(mNativeCallbacksDelegates.GetFileDataCallback),
                QueryFileNameCallback = Marshal.GetFunctionPointerForDelegate(mNativeCallbacksDelegates.QueryFileNameCallback),
                NotificationCallback = Marshal.GetFunctionPointerForDelegate(mNativeCallbacksDelegates.NotificationCallback),
                CancelCommandCallback = Marshal.GetFunctionPointerForDelegate(mNativeCallbacksDelegates.CancelCommandCallback),
            };
            mCallbacksPin = GCHandle.Alloc(mNativeCallbacksIntptr, GCHandleType.Pinned);

            hr = Interop.ProjFs.PrjStartVirtualizing(path, mCallbacksPin.AddrOfPinnedObject(), IntPtr.Zero, IntPtr.Zero, out mContext);
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        public void Dispose()
        {
            mContext.Dispose();
            mCallbacksPin.Free();
            GC.KeepAlive(mNativeCallbacksDelegates);
        }

        Int32 StartDirectoryEnumerationCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData, in Guid enumerationId)
        {
            return 0;
        }

        Int32 EndDirectoryEnumerationCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData, in Guid enumerationId)
        {
            return 0;
        }

        Int32 GetDirectoryEnumerationCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData, in Guid enumerationId, string searchExpression, IntPtr dirEntryBufferHandle)
        {
            return 0;
        }

        Int32 GetPlaceholderInfoCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData)
        {
            return 0;
        }

        Int32 GetFileDataCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData, UInt64 byteOffset, UInt32 length)
        {
            return 0;
        }

        Int32 QueryFileNameCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData)
        {
            return 0;
        }

        Int32 NotificationCallback(
            in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData,
            bool isDirectory,
            Interop.ProjFs.PRJ_NOTIFICATION notification,
            string destinationFileName,
            ref Interop.ProjFs.PRJ_NOTIFICATION_PARAMETERS operationParameters)
        {
            return 0;
        }

        void CancelCommandCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData)
        {

        }
    }
}
