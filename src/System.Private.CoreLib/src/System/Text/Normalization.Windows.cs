// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace System.Text
{
    internal static partial class Normalization
    {
        public static bool IsNormalized(this string strInput, NormalizationForm normalizationForm)
        {
            if (strInput == null)
            {
                throw new ArgumentNullException(nameof(strInput));
            }
            Contract.EndContractBlock();

            // The only way to know if IsNormalizedString failed is through checking the Win32 last error
            Interop.mincore.SetLastError(Interop.ERROR_SUCCESS);
            bool result = Interop.mincore.IsNormalizedString((int)normalizationForm, strInput, strInput.Length);

            int lastError = Interop.mincore.GetLastError();
            switch (lastError)
            {
                case Interop.ERROR_SUCCESS:
                    break;

                case Interop.ERROR_INVALID_PARAMETER:
                case Interop.ERROR_NO_UNICODE_TRANSLATION:
                    throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex, nameof(strInput));

                case Interop.ERROR_NOT_ENOUGH_MEMORY:
                    throw new OutOfMemoryException(SR.Arg_OutOfMemoryException);

                default:
                    throw new InvalidOperationException(SR.Format(SR.UnknownError_Num, lastError));
            }

            return result;
        }

        public static string Normalize(this string strInput, NormalizationForm normalizationForm)
        {
            if (strInput == null)
            {
                throw new ArgumentNullException(nameof(strInput));
            }
            Contract.EndContractBlock();

            // we depend on Win32 last error when calling NormalizeString
            Interop.mincore.SetLastError(Interop.ERROR_SUCCESS);

            // Guess our buffer size first
            int iLength = Interop.mincore.NormalizeString((int)normalizationForm, strInput, strInput.Length, null, 0);

            int lastError = Interop.mincore.GetLastError();
            // Could have an error (actually it'd be quite hard to have an error here)
            if ((lastError != Interop.ERROR_SUCCESS) ||
                 iLength < 0)
            {
                if (lastError == Interop.ERROR_INVALID_PARAMETER)
                    throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex, nameof(strInput));

                // We shouldn't really be able to get here..., guessing length is
                // a trivial math function...
                // Can't really be Out of Memory, but just in case:
                if (lastError == Interop.ERROR_NOT_ENOUGH_MEMORY)
                    throw new OutOfMemoryException(SR.Arg_OutOfMemoryException);

                // Who knows what happened?  Not us!
                throw new InvalidOperationException(SR.Format(SR.UnknownError_Num, lastError));
            }

            // Don't break for empty strings (only possible for D & KD and not really possible at that)
            if (iLength == 0) return string.Empty;

            // Someplace to stick our buffer
            char[] cBuffer = null;

            for (;;)
            {
                // (re)allocation buffer and normalize string
                cBuffer = new char[iLength];

                // Reset last error
                Interop.mincore.SetLastError(Interop.ERROR_SUCCESS);
                iLength = Interop.mincore.NormalizeString((int)normalizationForm, strInput, strInput.Length, cBuffer, cBuffer.Length);
                lastError = Interop.mincore.GetLastError();

                if (lastError == Interop.ERROR_SUCCESS)
                    break;

                // Could have an error (actually it'd be quite hard to have an error here)
                switch (lastError)
                {
                    // Do appropriate stuff for the individual errors:
                    case Interop.ERROR_INSUFFICIENT_BUFFER:
                        iLength = Math.Abs(iLength);
                        Debug.Assert(iLength > cBuffer.Length, "Buffer overflow should have iLength > cBuffer.Length");
                        continue;

                    case Interop.ERROR_INVALID_PARAMETER:
                    case Interop.ERROR_NO_UNICODE_TRANSLATION:
                        // Illegal code point or order found.  Ie: FFFE or D800 D800, etc.
                        throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex, nameof(strInput));

                    case Interop.ERROR_NOT_ENOUGH_MEMORY:
                        throw new OutOfMemoryException(SR.Arg_OutOfMemoryException);

                    default:
                        // We shouldn't get here...
                        throw new InvalidOperationException(SR.Format(SR.UnknownError_Num, lastError));
                }
            }

            // Copy our buffer into our new string, which will be the appropriate size
            return new string(cBuffer, 0, iLength);
        }

        // -----------------------------
        // ---- PAL layer ends here ----
        // -----------------------------
    }
}
