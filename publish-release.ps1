$localChanges = git status --short
if ( $null -ne $localChanges ) {
    Write-Output "Uncommitted changes detected, aborting release."
    Exit 1
}

git fetch origin
$remoteChanges = git log HEAD..origin/main --oneline
if ( $null -ne $remoteChanges ) {
    Write-Output "The main branch is out of date, aborting release."
    Exit 2
}

$csproj = [xml](Get-Content ./CSharpRepl/CSharpRepl.csproj)
# Select the <Version> node directly. The project has more than one <PropertyGroup> (e.g. the RID-specific
# packaging group), so $csproj.Project.PropertyGroup is an array and .Version would yield an array with a
# blank entry — which interpolates into the tag name with a stray trailing space.
$version = $csproj.SelectSingleNode('//Project/PropertyGroup/Version').InnerText.Trim()

Write-Output "Reminder: Did you update the CHANGELOG.md?"
Write-Output "Press Enter to create tag ""v$version"" and publish to nuget.org"
Read-Host

git tag "v$version"
git push origin "v$version"
