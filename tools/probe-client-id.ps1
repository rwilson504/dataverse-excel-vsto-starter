# Does a given client ID resolve in public Entra, Entra Government, or both?
#
# The device-code endpoint validates client_id eagerly, so an app that does not exist in the
# directory returns AADSTS700016. No credentials and no sign-in are involved.
#
#   pwsh tools/probe-client-id.ps1 -ClientId <guid>

param(
    [string]$ClientId = '51f81489-12ee-4a9e-aaae-a2591f45987d'
)

$authorities = @(
    @{ Name = 'Public Entra ID';   Host = 'login.microsoftonline.com' }
    @{ Name = 'Entra Government';  Host = 'login.microsoftonline.us' }
)

foreach ($authority in $authorities) {
    $uri = "https://$($authority.Host)/common/oauth2/v2.0/devicecode"

    try {
        Invoke-WebRequest -Uri $uri -Method POST -ContentType 'application/x-www-form-urlencoded' `
            -Body "client_id=$ClientId&scope=openid" -ErrorAction Stop | Out-Null

        '{0,-18} {1,-28} RESOLVES' -f $authority.Name, $authority.Host
    }
    catch {
        $message = $_.ErrorDetails.Message
        $code = if ($message -match 'AADSTS\d+') { $Matches[0] } else { "HTTP $($_.Exception.Response.StatusCode.value__)" }

        '{0,-18} {1,-28} {2}' -f $authority.Name, $authority.Host, $code
    }
}
