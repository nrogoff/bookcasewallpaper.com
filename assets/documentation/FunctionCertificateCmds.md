Functions Core Tools build can’t auto-create HTTPS certs, so you need to provide one manually.

Run this once in api PowerShell:

```PowerShell
Set-Location "C:\gitrepos\copilot-worktrees\bookcasewallpaper.com\nrogoff-fluffy-couscous\api"

$plain = "change-this-password"
$pwd = ConvertTo-SecureString -String $plain -Force -AsPlainText

$cert = New-SelfSignedCertificate `
  -Subject "CN=localhost" `
  -DnsName "localhost" `
  -FriendlyName "Functions Development" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -KeyUsage DigitalSignature `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1")

Export-PfxCertificate -Cert $cert -FilePath ".\certificate.pfx" -Password $pwd
Import-PfxCertificate -FilePath ".\certificate.pfx" -CertStoreLocation "Cert:\CurrentUser\Root" -Password $pwd
```

Then start API with that cert:

```PowerShell
func start --useHttps --cert .\certificate.pfx --password "3D1qMWRpIKw78ncl"
```