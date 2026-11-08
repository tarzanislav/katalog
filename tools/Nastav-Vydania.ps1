# KATALOG - nastavenie vydani JEDNYM KROKOM: prostredie "release" so schvalovanim + podpisovy kluc vydani.
# Spustite vo Windows PowerShell 5.1 alebo PowerShell 7: cely obsah vlozte do okna PowerShell (je to jeden blok & { }).
#  - Ak je nainstalovany a prihlaseny GitHub CLI (gh), skript urobi vsetko sam.
#  - Inak sam otvori presne stranky GitHubu a povie, co kliknut.
# Sukromny kluc vznikne len na tomto pocitaci, nikam sa neuklada a ide jedine do GitHubu (ako tajomstvo prostredia).
# Na konci skript vypise verejny kluc a da ho do schranky - ten nie je tajny, poslite ho Claudovi.
# Texty su bez diakritiky (konzola Windows).
& {
    $ErrorActionPreference = 'Stop'
    $repo = 'tarzanislav/katalog'
    $secretName = 'KATALOG_RELEASE_KEY'
    function FromHex([string]$hex) { [byte[]]($hex -split '(..)' -ne '' | ForEach-Object { [Convert]::ToByte($_, 16) }) }

    # 1. novy kluc ECDSA P-256 (verejna cast: SubjectPublicKeyInfo, sukromna: PKCS#8)
    $key = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
    try {
        $p = $key.ExportParameters($true)
        $point = [byte[]](@([byte]4) + $p.Q.X + $p.Q.Y)
        $public = [byte[]]((FromHex '3059301306072a8648ce3d020106082a8648ce3d030107034200') + $point)
        $private = [byte[]]((FromHex '308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b0201010420') + $p.D + (FromHex 'a144034200') + $point)
        $privateText = [Convert]::ToBase64String($private)
        [Array]::Clear($private, 0, $private.Length)
        [Array]::Clear($p.D, 0, $p.D.Length)
    } finally {
        $key.Dispose()
    }
    $publicText = [Convert]::ToBase64String($public)

    # 2. je GitHub CLI pripraveny?
    $auto = $false
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        & gh auth status *> $null
        $auto = ($LASTEXITCODE -eq 0)
    }

    if ($auto) {
        Write-Host ''
        Write-Host 'GitHub CLI je prihlaseny: nastavujem vsetko automaticky...' -ForegroundColor Cyan
        $id = (& gh api user --jq .id).Trim()
        $body = '{"reviewers":[{"type":"User","id":' + $id + '}],"prevent_self_review":false}'
        $body | & gh api -X PUT "repos/$repo/environments/release" --input - | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Prostredie release sa nepodarilo vytvorit.' }
        $privateText | & gh secret set $secretName --env release --repo $repo
        if ($LASTEXITCODE -ne 0) { throw 'Tajomstvo sa nepodarilo ulozit.' }
        Write-Host 'Hotovo: prostredie release so schvalovanim a tajomstvo su nastavene.' -ForegroundColor Green
    } else {
        Write-Host ''
        Write-Host 'KROK A: otvaram stranku GitHubu na vytvorenie prostredia.' -ForegroundColor Green
        Write-Host '  Name: release  ->  Configure environment'
        Write-Host '  zaskrtnite Required reviewers  ->  napiste tarzanislav a vyberte  ->  Save protection rules'
        Write-Host '  (Prevent self-review nechajte NEzaskrtnute)'
        Start-Process "https://github.com/$repo/settings/environments/new"
        $null = Read-Host 'Hotovo? Stlacte Enter'
        do {
            Set-Clipboard -Value $privateText
            Write-Host ''
            Write-Host 'KROK B: Sukromny kluc je v schranke. Otvaram stranku prostredi.' -ForegroundColor Green
            Write-Host "  release  ->  Add environment secret  ->  Name: $secretName  ->  Value: Ctrl+V  ->  Add secret"
            Write-Host '  Medzitym NIC nekopirujte a nerobte snimku obrazovky.' -ForegroundColor Yellow
            Start-Process "https://github.com/$repo/settings/environments"
            $answer = Read-Host 'Ulozene? Napiste H a Enter. Skopirovat znova? Len Enter'
        } until ($answer -match '^[hH]')
    }

    $privateText = $null
    # schranka: sukromny kluc sa prepise verejnym (ten nie je tajny)
    Set-Clipboard -Value $publicText
    Write-Host ''
    Write-Host 'HOTOVO. Verejny kluc je v schranke - vlozte ho Claudovi (Ctrl+V):' -ForegroundColor Cyan
    Write-Host $publicText
    Write-Host ''
}
