# Programas de terceiros (não vêm no repositório)

O `ReparoSysPDV.exe` leva dentro dele alguns programas que coloca na pasta do SysPDV
(`docs/decisoes.md`, D-015 e D-016). Eles **não são deste projeto** e por isso não vão para o
GitHub (D-022). Antes de compilar, copie-os para esta pasta, exatamente com estes nomes:

```
terceiros/
  firebird/
    fbclient.dll
    firebird.msg
    gbak.exe
    gfix.exe
  syspdv/
    dbMonitor.exe
    VerificaIndice.exe
```

| Arquivo | De onde vem | Tamanho (bytes) | SHA256 |
|---|---|---|---|
| `firebird/fbclient.dll` | Firebird 2.5.1.26351, 32 bits | 548864 | `96303A1B2133F1B6FA90240D3ACC0A2BE291473CF5CC1F72FD89F5B65CCA9286` |
| `firebird/firebird.msg` | Firebird 2.5.1.26351 | 148556 | `190E1ADA19F7E051F96A09F674244831FFD0E071A3E83ED296AEDE062AF74C93` |
| `firebird/gbak.exe` | Firebird 2.5.1.26351, 32 bits | 270336 | `82AC13C9158B2149115D931A7545ADC53032364621B242754CF3D2ED105F9FB2` |
| `firebird/gfix.exe` | Firebird 2.5.1.26351, 32 bits | 110592 | `C1DCF8BA788171512975EF38778FFB925B1B33924E0660BF4AD58C45AD8D1FB4` |
| `syspdv/dbMonitor.exe` | Devart dbMonitor 3.0.3.0 (pasta do SysPDV) | 2683904 | `EF456B09B821F3CF8B8463896933C7A374F090EB9365E68F431084A6F09243B3` |
| `syspdv/VerificaIndice.exe` | SysPDV | 279552 | `EB96BE5C5091F99C0E36330C75AF937A6CA2DD841F3D6C6C085B235C33405837` |

Os arquivos do Firebird 2.5.1 vêm da instalação do Firebird (pasta `bin`); os do SysPDV, de uma
instalação do SysPDV. Para conferir se é o arquivo certo:

```powershell
Get-FileHash terceiros\firebird\gbak.exe -Algorithm SHA256
```

Se algum faltar, o build para com a mensagem *"Falta o arquivo ... veja terceiros\LEIA-ME.md"*.
