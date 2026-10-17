# PowerShell 5 CodeDom writes a compiler output into TEMP and fails for some
# Unicode paths. Emit the P/Invoke declarations in memory: no compiler, temp
# assembly, environment override or additional runtime is required.
function New-InstalledQualificationProcessJob {
    if (-not ('VideoMakerInstalledQualificationJobNative' -as [type])) {
        $name = [Reflection.AssemblyName]::new('VideoMaker.InstalledQualification.NativeJob')
        $access = [Reflection.Emit.AssemblyBuilderAccess]::Run
        if ($PSVersionTable.PSEdition -eq 'Desktop') {
            $assembly = [AppDomain]::CurrentDomain.DefineDynamicAssembly($name, $access)
        } else {
            $assembly = [Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly($name, $access)
        }
        $module = $assembly.DefineDynamicModule($name.Name)
        $builder = $module.DefineType('VideoMakerInstalledQualificationJobNative',
            [Reflection.TypeAttributes]'Public, Abstract, Sealed')
        $methods = @(
            @{ Name = 'CreateJobObjectW'; Return = [IntPtr]; Parameters = [Type[]]@([IntPtr], [string]) },
            @{ Name = 'SetInformationJobObject'; Return = [bool]; Parameters = [Type[]]@([IntPtr], [int], [IntPtr], [uint32]) },
            @{ Name = 'AssignProcessToJobObject'; Return = [bool]; Parameters = [Type[]]@([IntPtr], [IntPtr]) },
            @{ Name = 'CloseHandle'; Return = [bool]; Parameters = [Type[]]@([IntPtr]) }
        )
        foreach ($signature in $methods) {
            $method = $builder.DefineMethod($signature.Name,
                [Reflection.MethodAttributes]'Public, Static, PinvokeImpl', [Reflection.CallingConventions]::Standard,
                $signature.Return, $signature.Parameters)
            $method.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
            $import = [Runtime.InteropServices.DllImportAttribute]
            $attribute = [Reflection.Emit.CustomAttributeBuilder]::new(
                $import.GetConstructor([Type[]]@([string])), [object[]]@('kernel32.dll'),
                [Reflection.FieldInfo[]]@($import.GetField('SetLastError'), $import.GetField('CharSet'),
                    $import.GetField('CallingConvention'), $import.GetField('ExactSpelling'), $import.GetField('PreserveSig')),
                [object[]]@($true, [Runtime.InteropServices.CharSet]::Unicode,
                    [Runtime.InteropServices.CallingConvention]::Winapi, $true, $true))
            $method.SetCustomAttribute($attribute)
        }
        $null = $builder.CreateType()
    }
    $handle = [VideoMakerInstalledQualificationJobNative]::CreateJobObjectW([IntPtr]::Zero, $null)
    if ($handle -eq [IntPtr]::Zero) { throw 'Cannot create the qualification process job.' }
    $limits = [IntPtr]::Zero
    try {
        # JOBOBJECT_EXTENDED_LIMIT_INFORMATION: 144 bytes on x64, 112 on x86.
        # LimitFlags is at byte 16 on both architectures. Other fields are zero.
        $size = if ([IntPtr]::Size -eq 8) { 144 } else { 112 }
        $limits = [Runtime.InteropServices.Marshal]::AllocHGlobal($size)
        [Runtime.InteropServices.Marshal]::Copy([byte[]]::new($size), 0, $limits, $size)
        [Runtime.InteropServices.Marshal]::WriteInt32($limits, 16, 0x2000)
        if (-not [VideoMakerInstalledQualificationJobNative]::SetInformationJobObject($handle, 9, $limits, $size)) {
            throw 'Cannot enable process cleanup for the qualification job.'
        }
        $job = [pscustomobject]@{ Handle = $handle }
        $job | Add-Member -MemberType ScriptMethod -Name Assign -Value {
            param([Diagnostics.Process]$Process)
            try {
                if (-not [VideoMakerInstalledQualificationJobNative]::AssignProcessToJobObject($this.Handle, $Process.Handle)) {
                    throw 'Cannot attach the qualification process to its job.'
                }
            } catch {
                # An unattached child is not owned by this job yet. Dispose
                # cannot stop it: terminate the exact Process we just started.
                # PowerShell 5 has only Kill(); desktop workers have separate
                # lifetime handling. Newer runtimes also kill the tree.
                if (-not $Process.HasExited) {
                    if ($null -ne $Process.GetType().GetMethod('Kill', [Type[]]@([bool]))) {
                        $Process.Kill($true)
                    } else {
                        $Process.Kill()
                    }
                    if (-not $Process.WaitForExit(5000)) { throw 'Cannot stop the unattached qualification process.' }
                }
                throw
            }
        }
        $job | Add-Member -MemberType ScriptMethod -Name Dispose -Value {
            if ($this.Handle -ne [IntPtr]::Zero) {
                $null = [VideoMakerInstalledQualificationJobNative]::CloseHandle($this.Handle)
                $this.Handle = [IntPtr]::Zero
            }
        }
        return $job
    } catch {
        $null = [VideoMakerInstalledQualificationJobNative]::CloseHandle($handle)
        throw
    } finally {
        if ($limits -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::FreeHGlobal($limits) }
    }
}
Export-ModuleMember -Function New-InstalledQualificationProcessJob
