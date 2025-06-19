Param (
    [string] $configuration = 'Release',
	[string[]] $profiles = @('framework-dependent-win-x64', 'framework-dependent-linux-x64')#@('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'framework-dependent-win-x64', 'framework-dependent-linux-x64')
)

git submodule update --init --remote --merge --recursive

foreach ($profile in $profiles) {
    $publishDir = "./build/$configuration/$profile"

	Write-Host "Publishing $profile"
	if ($profile -eq 'framework-dependent') {
		dotnet publish ViennaDotNet.sln -o $publishDir --no-self-contained -c $configuration /p:PublishSingleFile=false
	}
	elseif ($profile -like 'framework-dependent-*') {
		dotnet publish ViennaDotNet.sln -o $publishDir --no-self-contained -c $configuration -r $profile.Substring('framework-dependent-'.Length) /p:PublishSingleFile=false
	}
	else {
		dotnet publish ViennaDotNet.sln -o $publishDir --sc -c $configuration -r $profile
	}

    Copy-Item -Path "staticdata" -Destination "$publishDir/staticdata" -Recurse -Force
}