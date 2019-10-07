﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Tricycle.Diagnostics
{
    /// <summary>
    /// Wraps <see cref="Process"/> so that it implements <see cref="IProcess"/>.
    /// </summary>
    /// <seealso cref="IProcess"/>
    public class ProcessWrapper : IProcess
    {
        readonly Process _process;

        public int ExitCode => _process.ExitCode;
        public bool HasExited => _process.HasExited;

        public event Action Exited;
        public event Action<string> ErrorDataReceived;
        public event Action<string> OutputDataReceived;

        public ProcessWrapper()
        {
            _process = new Process()
            {
                EnableRaisingEvents = true
            };

            _process.Exited += (sender, e) => Exited?.Invoke();
            _process.ErrorDataReceived += (sender, e) =>
            {
                Debug.WriteLine(e?.Data);
                ErrorDataReceived?.Invoke(e?.Data);
            };
            _process.OutputDataReceived += (sender, e) =>
            {
                Debug.WriteLine(e?.Data);
                OutputDataReceived?.Invoke(e?.Data);
            };
        }

        public void Dispose()
        {
            _process.Dispose();
        }

        public void Kill()
        {
            try
            {
                _process.Kill();
            }
            catch (NotSupportedException ex)
            {
                Debug.WriteLine(ex);
                throw new InvalidOperationException("An error occurred killing the process.", ex);
            }
            catch (Win32Exception ex)
            {
                Debug.WriteLine(ex);
                throw new InvalidOperationException("An error occurred killing the process.", ex);
            }
        }

        public bool Start(ProcessStartInfo startInfo)
        {
            if (startInfo == null)
            {
                throw new ArgumentNullException(nameof(startInfo));
            }

            _process.StartInfo = startInfo;

            Debug.WriteLine($"Starting process: {startInfo.FileName} {startInfo.Arguments}");

            try
            {
                bool result = _process.Start();

                if (!startInfo.UseShellExecute)
                {
                    if (startInfo.RedirectStandardOutput)
                    {
                        _process.BeginOutputReadLine();
                    }

                    if (startInfo.RedirectStandardError)
                    {
                        _process.BeginErrorReadLine();
                    }
                }

                return result;
            }
            catch (ObjectDisposedException ex)
            {
                Debug.WriteLine(ex);
                throw new InvalidOperationException("An error occurred starting the process.", ex);
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine(ex);
                throw new ArgumentException($"{nameof(startInfo)} is invalid.", ex);
            }
            catch (FileNotFoundException ex)
            {
                Debug.WriteLine(ex);
                throw new InvalidOperationException("An error occurred starting the process.", ex);
            }
            catch (Win32Exception ex)
            {
                Debug.WriteLine(ex);
                throw new InvalidOperationException("An error occurred starting the process.", ex);
            }
        }

        public void WaitForExit()
        {
            WaitForExit(-1);
        }

        public bool WaitForExit(int milliseconds)
        {
            try
            {
                if (milliseconds > 0)
                {
                    if (_process.WaitForExit(milliseconds))
                    {
                        // This is a workaround for a bug in the .NET code.
                        // See https://stackoverflow.com/a/25772586/9090758 for more details.
                        _process.WaitForExit();
                        return true;
                    }

                    return false;
                }

                _process.WaitForExit();
                return true;
            }
            catch (Win32Exception ex)
            {
                Debug.WriteLine(ex);
                throw new InvalidOperationException("An error occurred waiting for the process.", ex);
            }
            catch (SystemException ex)
            {
                Debug.WriteLine(ex);
                throw new InvalidOperationException("An error occurred waiting for the process.", ex);
            }
        }
    }
}
