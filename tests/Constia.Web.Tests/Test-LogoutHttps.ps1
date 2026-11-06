#Requires -Version 7
param(
    [Parameter(Mandatory)] [uri] $BaseUrl,
    [Parameter(Mandatory)] [string] $Email,
    [Parameter(Mandatory)] [securestring] $Password,
    [switch] $SkipCertificateCheck
)
$ErrorActionPreference = 'Stop'

if ($BaseUrl.Scheme -ne 'https' -or !$BaseUrl.IsLoopback -or $BaseUrl.AbsolutePath -ne '/') {
    throw 'Usá el origen HTTPS de Constia.Web en localhost con una cuenta de prueba.'
}

function New-Session {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    if ($SkipCertificateCheck) {
        $handler.ServerCertificateCustomValidationCallback = [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    }
    return @{ Cookies = $handler.CookieContainer; Client = [System.Net.Http.HttpClient]::new($handler) }
}

function Invoke-Web($path, $session, $form = $null) {
    $uri = [uri]::new($BaseUrl, $path)
    if ($null -eq $form) {
        $response = $session.Client.GetAsync($uri).GetAwaiter().GetResult()
    } else {
        $fields = [System.Collections.Generic.Dictionary[string,string]]::new()
        foreach ($key in $form.Keys) { $fields[$key] = $form[$key] }
        $body = [System.Net.Http.FormUrlEncodedContent]::new($fields)
        try { $response = $session.Client.PostAsync($uri, $body).GetAwaiter().GetResult() }
        finally { $body.Dispose() }
    }
    try {
        return @{ StatusCode = [int]$response.StatusCode; Content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
    } finally {
        $response.Dispose()
    }
}

function Get-Antiforgery($response) {
    if ($response.StatusCode -ne 200 -or $response.Content -notmatch 'name="__RequestVerificationToken" value="([^"]+)"') {
        throw 'No se encontró el token de antifalsificación en la respuesta esperada.'
    }
    return [System.Net.WebUtility]::HtmlDecode($Matches[1])
}

function Assert-Status($response, $expected, $step) {
    if ($response.StatusCode -ne $expected) {
        throw "$step devolvió HTTP $($response.StatusCode), esperado $expected."
    }
}

function Start-Session($session) {
    $token = Get-Antiforgery (Invoke-Web '/login' $session)
    $credential = [pscredential]::new($Email, $Password)
    $login = Invoke-Web '/login' $session @{
        _handler = 'login'
        __RequestVerificationToken = $token
        'Datos.Email' = $Email
        'Datos.Password' = $credential.GetNetworkCredential().Password
    }
    Assert-Status $login 302 'Login'
}

$original = New-Session
$independent = New-Session
$copy = New-Session
try {
    Start-Session $original
    Start-Session $independent
    $cookie = $original.Cookies.GetCookies($BaseUrl)['__Host-Constia.Session']
    if (!$cookie -or !$cookie.Secure -or !$cookie.HttpOnly) {
        throw 'No se recibió una cookie de sesión Secure y HttpOnly.'
    }
    $copy.Cookies.Add($BaseUrl, [System.Net.Cookie]::new($cookie.Name, $cookie.Value, '/'))
    Assert-Status (Invoke-Web '/cuenta' $copy) 200 'Copia antes del logout'
    Assert-Status (Invoke-Web '/cuenta' $independent) 200 'Sesión independiente antes del logout'
    Assert-Status (Invoke-Web '/logout' $original @{}) 400 'Logout sin antifalsificación'
    Assert-Status (Invoke-Web '/cuenta' $copy) 200 'Copia después del logout inválido'

    $token = Get-Antiforgery (Invoke-Web '/cuenta' $original)
    Assert-Status (Invoke-Web '/logout' $original @{ __RequestVerificationToken = $token }) 302 'Logout válido'
    Assert-Status (Invoke-Web '/cuenta' $original) 302 'Sesión original después del logout'
    Assert-Status (Invoke-Web '/cuenta' $copy) 302 'Copia después del logout'
    Assert-Status (Invoke-Web '/cuenta' $independent) 200 'Sesión independiente después del logout'
    'PASS HTTPS: copia aceptada antes del logout y rechazada después; sesión independiente conservada.'
} finally {
    foreach ($session in @($original, $independent, $copy)) { $session.Client.Dispose() }
}
