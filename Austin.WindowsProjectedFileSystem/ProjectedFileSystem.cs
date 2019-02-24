using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    public partial class ProjectedFileSystem : IDisposable
    {
        readonly Guid mUniqueId;
        readonly IProjectedFileSystemCallbacks mCallbacks;
        readonly Interop.ProjFs.PRJ_CALLBACKS mNativeCallbacksDelegates;
        readonly Interop.ProjFs.PRJ_CALLBACKS_INTPTR mNativeCallbacksIntptr;
        readonly Interop.ProjFs.PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT mContext;
        readonly Dictionary<Guid, EnumerationStatus> mDirEnumInfo;

        GCHandle mCallbacksPin;

        public ProjectedFileSystem(string path, IProjectedFileSystemCallbacks callbacks)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (callbacks == null)
                throw new ArgumentNullException(nameof(callbacks));

            mCallbacks = callbacks;
            mUniqueId = Guid.NewGuid();
            mDirEnumInfo = new Dictionary<Guid, EnumerationStatus>();

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

        public bool FileNameMatch(string fileNameToCheck, string pattern)
        {
            return Interop.ProjFs.PrjFileNameMatch(fileNameToCheck, pattern);
        }

        public void Dispose()
        {
            mContext.Dispose();
            mCallbacksPin.Free();
            GC.KeepAlive(this);
        }

        Int32 StartDirectoryEnumerationCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData, in Guid enumerationId)
        {
            try
            {
                lock (mDirEnumInfo)
                {
                    mDirEnumInfo[enumerationId] = new EnumerationStatus();
                }
                return 0;
            }
            catch (Exception ex)
            {
                return Marshal.GetHRForException(ex);
            }
        }

        Int32 EndDirectoryEnumerationCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData, in Guid enumerationId)
        {
            try
            {
                lock (mDirEnumInfo)
                {
                    return mDirEnumInfo.Remove(enumerationId) ? 0 : Interop.HResult.ERROR_INVALID_PARAMETER;
                }
            }
            catch (Exception ex)
            {
                return Marshal.GetHRForException(ex);
            }
        }

        Int32 GetDirectoryEnumerationCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData, in Guid enumerationId, string searchExpression, IntPtr dirEntryBufferHandle)
        {
            try
            {
                EnumerationStatus enumStatus;
                lock (mDirEnumInfo)
                {
                    if (!mDirEnumInfo.TryGetValue(enumerationId, out enumStatus))
                        return Interop.HResult.ERROR_INVALID_PARAMETER;
                }

                if ((callbackData.Flags & Interop.ProjFs.PRJ_CALLBACK_DATA_FLAGS.RESTART_SCAN) != 0)
                {
                    if (enumStatus.Entries == null)
                    {
                        bool isWildCard = Interop.ProjFs.PrjDoesNameContainWildCards(searchExpression);
                        enumStatus.Entries = mCallbacks.EnumerateDirectory(isWildCard, searchExpression);
                    }
                    enumStatus.CurrentIndex = 0;
                }


                for (; enumStatus.CurrentIndex < enumStatus.Entries.Length; enumStatus.CurrentIndex++)
                {
                    var item = enumStatus.Entries[enumStatus.CurrentIndex];

                    var fileInfo = new Interop.ProjFs.PRJ_FILE_BASIC_INFO()
                    {
                        IsDirectory = item.IsDirectory,
                        FileSize = item.FileSize,
                    };

                    int hr = Interop.ProjFs.PrjFillDirEntryBuffer(item.Name, fileInfo, dirEntryBufferHandle);
                    if (hr == Interop.HResult.ERROR_INSUFFICIENT_BUFFER)
                    {
                        if (enumStatus.CurrentIndex == 0)
                            return Interop.HResult.ERROR_INSUFFICIENT_BUFFER;
                        break;
                    }
                    else if (hr != 0)
                    {
                        Debug.Fail($"Unexpected HR: {hr:x}");
                    }

                    if ((callbackData.Flags & Interop.ProjFs.PRJ_CALLBACK_DATA_FLAGS.RETURN_SINGLE_ENTRY) != 0)
                        break;
                }

                return 0;
            }
            catch (Exception ex)
            {
                return Marshal.GetHRForException(ex);
            }
        }

        Int32 GetPlaceholderInfoCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData)
        {
            try
            {
                return 0;
            }
            catch (Exception ex)
            {
                return Marshal.GetHRForException(ex);
            }
        }

        Int32 GetFileDataCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData, UInt64 byteOffset, UInt32 length)
        {
            try
            {
                return 0;
            }
            catch (Exception ex)
            {
                return Marshal.GetHRForException(ex);
            }
        }

        Int32 QueryFileNameCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData)
        {
            try
            {
                return mCallbacks.FileExists(callbackData.FilePathName) ? 0 : Interop.HResult.ERROR_FILE_NOT_FOUND;
            }
            catch (Exception ex)
            {
                return Marshal.GetHRForException(ex);
            }
        }

        Int32 NotificationCallback(
            in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData,
            bool isDirectory,
            Interop.ProjFs.PRJ_NOTIFICATION notification,
            string destinationFileName,
            ref Interop.ProjFs.PRJ_NOTIFICATION_PARAMETERS operationParameters)
        {
            int hr = 0;
            try
            {
                switch (notification)
                {
                    case Interop.ProjFs.PRJ_NOTIFICATION.FILE_OPENED:
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.NEW_FILE_CREATED:
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.FILE_OVERWRITTEN:
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.PRE_DELETE:
                        hr = Interop.HResult.STATUS_CANNOT_DELETE;
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.PRE_RENAME:
                        hr = Interop.HResult.STATUS_CANNOT_DELETE;
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.PRE_SET_HARDLINK:
                        hr = Interop.HResult.STATUS_CANNOT_DELETE;
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.FILE_RENAMED:
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.HARDLINK_CREATED:
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.FILE_HANDLE_CLOSED_NO_MODIFICATION:
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.FILE_HANDLE_CLOSED_FILE_MODIFIED:
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.FILE_HANDLE_CLOSED_FILE_DELETED:
                        break;
                    case Interop.ProjFs.PRJ_NOTIFICATION.FILE_PRE_CONVERT_TO_FULL:
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                hr = Marshal.GetHRForException(ex);
            }
            return hr;
        }

        void CancelCommandCallback(in Interop.ProjFs.PRJ_CALLBACK_DATA callbackData)
        {
            try
            {
            }
            catch
            {
            }
        }
    }
}
