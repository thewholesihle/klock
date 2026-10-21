Place the installers here before deploying (Netlify serves them as downloads):

  Klock-Setup-0.1.0-x64.exe     (from ..\dist after running build.ps1)
  Klock-Setup-0.1.0-arm64.exe

The download buttons in index.html point at these exact file names. If you host the installers elsewhere
(GitHub Releases, a CDN), change the two href values in the #download section instead.
Installers are large and unsigned preview builds: do not commit them to a public git repository.
