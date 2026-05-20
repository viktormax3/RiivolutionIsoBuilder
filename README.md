# Riivolution ISO Builder

Aplicacion C# WinForms para convertir mods Riivolution en imagenes modificadas de Wii usando `wit` y `wstrt`.

## Estructura

- `src/RiivolutionIsoBuilder.App`: aplicacion WinForms.
- `data/tools`: binarios preservados de Wiimms (`wit.exe`, `wstrt.exe`) y sus DLL.
- `data/mods`: archivos `.zip` de mods.
- `data/xml`, `data/gct`, `data/banner`: parches y recursos.
- `games`: carpeta recomendada para colocar backups de prueba.
- `output`: imagenes generadas.
- `work`: temporales de construccion.

La carpeta `Base` ya no es obligatoria para los recursos internos. Durante la migracion, la app aun escanea `Base` si existe junto a este proyecto, para facilitar pruebas con imagenes ya colocadas alli.

Los XML Riivolution nativos usan el nombre visible de su menu, por ejemplo el primer `section name`, para mostrarse en la interfaz y nombrar la salida.

## Ejecutar

```powershell
dotnet run --project .\src\RiivolutionIsoBuilder.App\RiivolutionIsoBuilder.App.csproj
```

## Notas de dependencias

Los mods se extraen con `System.IO.Compression`, asi que no hay DLL externa de compresion. El formato soportado para paquetes locales es ZIP.

