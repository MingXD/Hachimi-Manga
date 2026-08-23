$rx = '^\s*(private|public|protected|internal)\s+(async\s+)?[\w<>\[\],.? ]+\s+(\w+)\s*\('
foreach ($t in @("    private async void ShowJumpDialog()", "    private void OnPageTextClick(object? sender, RoutedEventArgs e)", "    private void EnterWebtoon()")) {
    if ($t -match $rx) { Write-Output ("MATCH g3=[{0}]  <- {1}" -f $Matches[3], $t) } else { Write-Output ("NOMATCH       <- {0}" -f $t) }
}
