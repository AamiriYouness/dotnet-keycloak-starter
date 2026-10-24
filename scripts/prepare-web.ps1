$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$certificatePassword = $env:HTTPS_CERT_PASSWORD
if ([string]::IsNullOrWhiteSpace($certificatePassword)) {
    $certificatePassword = "local-demo-certificate-password"
}

$certificateDirectory = Join-Path (Get-Location) ".local/certs"
$certificatePath = Join-Path $certificateDirectory "web.pfx"
New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath $certificatePath)) {
    & dotnet dev-certs https --export-path $certificatePath --password $certificatePassword
    if ($LASTEXITCODE -ne 0) {
        throw "The development certificate could not be exported."
    }
}

if ($env:OS -eq "Windows_NT") {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $acl = Get-Acl -LiteralPath $certificatePath
    $acl.SetAccessRuleProtection($true, $false)
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $identity,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.AccessControlType]::Allow)
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $certificatePath -AclObject $acl
}
else {
    & chmod 600 $certificatePath
    if ($LASTEXITCODE -ne 0) {
        throw "The certificate file permissions could not be restricted."
    }
}

Write-Host "Development HTTPS certificate is ready. Keep .local/certs with the authentication volume."
