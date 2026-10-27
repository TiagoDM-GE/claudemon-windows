# Claudemon para Windows

Mostra o uso da tua subscrição do **Claude Code** em tempo real na área de notificação do Windows: sessão de 5 horas, semana (todos os modelos) e semana por modelo, com contagem decrescente até à reposição e alertas quando a quota está a acabar.

Versão para Windows do [Claudemon](https://github.com/ardabalkandev/claudemon) (macOS, licença MIT).

> **Ferramenta não oficial.** Não tem qualquer ligação à Anthropic nem é suportada por ela. "Claude" e "Claude Code" são marcas da Anthropic. A app limita-se a ler o que o CLI oficial `claude`, instalado e autenticado por ti, já mostra.

---

## Índice

1. [Funcionalidades](#funcionalidades)
2. [Requisitos](#requisitos)
3. [Instalação](#instalação)
   - [Opção A: descarregar o .exe pronto](#opção-a-descarregar-o-exe-pronto)
   - [Opção B: compilar a partir do código](#opção-b-compilar-a-partir-do-código)
4. [Primeira utilização](#primeira-utilização)
5. [Utilização](#utilização)
6. [Resolução de problemas](#resolução-de-problemas)
7. [Desinstalar](#desinstalar)
8. [Como funciona](#como-funciona)
9. [Desenvolvimento](#desenvolvimento)
10. [Privacidade](#privacidade)
11. [Licença](#licença)

---

## Funcionalidades

| | |
|---|---|
| **Ícone na área de notificação** | Desenhado em tempo real em três estilos à escolha: **Anel** (progresso da sessão), **%** (número da sessão com barra por baixo) ou **Barras** (sessão e semana lado a lado). As cores são verde (< 60 %), amarelo (60–85 %) e vermelho (≥ 86 %), e um ponto vermelho indica erro. Ao passar o rato aparece o resumo. |
| **Painel** | Abre com um clique no ícone. Mostra os três limites com barra, percentagem, "Repõe em 3h 12m" e a hora de reposição no fuso do limite. O nome do modelo da terceira linha vem do texto do CLI, não está fixo no código. |
| **Janela flutuante** | Opcional e sempre visível. Arrasta-a para a mover, faz **duplo-clique** para alternar entre a vista completa e a compacta (só o anel) e usa o botão direito para as opções. A posição fica guardada. |
| **Alertas** | Notificações do Windows quando restam 25 %, quando restam 5 % e quando o limite esgota, além de um aviso quando um limite que usaste é reposto. Cada alerta só aparece uma vez por janela de quota e todos podem ser desligados individualmente. |
| **Iniciar com o Windows** | Usa a chave `Run` do utilizador e não precisa de permissões de administrador. |
| **Modo escuro** | Tema **Sistema** (segue o Windows), **Claro** ou **Escuro**, escolhido na ⚙ do painel. Muda na hora, no painel e na janela flutuante. O ícone acompanha sempre a cor da barra de tarefas. |
| **Idioma** | Português ou inglês, conforme o idioma do Windows. Cantos arredondados no Windows 11. |

---

## Requisitos

- **Windows 10 (versão 1809 ou mais recente) ou Windows 11**, 64 bits.
- **Claude Code instalado e com sessão iniciada** na tua subscrição (Pro/Max/Team). A app não chama nenhuma API: corre `claude` localmente e mostra **o teu** uso.
  - Se ainda não o tens, segue as instruções em <https://claude.ai/download> ou instala-o via npm: `npm install -g @anthropic-ai/claude-code`.
  - Depois abre um terminal, corre `claude` e faz `/login`.
- Só para a versão pequena do `.exe` (ver abaixo): o **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**.

---

## Instalação

### Opção A: descarregar o .exe pronto

1. No GitHub, abre o separador **Releases** deste repositório. Se ainda não houver nenhuma release, abre **Actions → build → a execução mais recente → Artifacts → `ClaudemonWin-win-x64`**.
2. Escolhe um dos ficheiros:

   | Ficheiro | Tamanho | Precisa do .NET Desktop Runtime? |
   |---|---|---|
   | `ClaudemonWin.exe` | ~1 MB | Sim. Se faltar, o Windows oferece-se para o instalar. |
   | `ClaudemonWin-standalone.exe` | ~70 MB | Não |

3. Copia o `.exe` para uma pasta fixa, por exemplo `%LOCALAPPDATA%\Programs\ClaudemonWin\`. Evita deixá-lo em *Transferências*, porque o arranque automático aponta para o caminho onde o `.exe` está.
4. Faz duplo-clique. Se aparecer o aviso do SmartScreen, vê [o Windows protegeu o seu PC](#o-windows-protegeu-o-seu-pc-smartscreen).

### Opção B: compilar a partir do código

**1. Instalar as ferramentas** (uma vez só):

```powershell
winget install Microsoft.DotNet.SDK.8
winget install Git.Git
```

Fecha e volta a abrir o PowerShell para o `dotnet` e o `git` ficarem no `PATH`.

**2. Obter o código:**

```powershell
git clone https://github.com/<o-teu-utilizador>/claudemon-windows.git
cd claudemon-windows
```

O repositório é privado, por isso o Git pede para iniciares sessão no GitHub da primeira vez.

Em alternativa, usa **Code → Download ZIP** no GitHub e extrai o ficheiro. Nesse caso vê também [o script não está assinado digitalmente](#o-script-não-está-assinado-digitalmente).

**3. Compilar e testar:**

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

O script corre os testes e gera `dist\ClaudemonWin.exe`. Para gerar o `.exe` autónomo, que não precisa do runtime:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -SelfContained
```

<details>
<summary>Sem o script (comandos manuais)</summary>

```powershell
dotnet test tests/ClaudemonWin.Core.Tests -c Release
dotnet publish src/ClaudemonWin -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

Para a versão autónoma, troca `--self-contained false` por `--self-contained true`.
</details>

**4.** Copia `dist\ClaudemonWin.exe` para uma pasta fixa (ver o passo 3 da opção A) e executa-o.

---

## Primeira utilização

1. Ao arrancar, aparece uma notificação "O Claudemon está a correr".
2. **Mantém o ícone visível.** O Windows 11 põe os ícones novos na zona escondida **`^`**, junto ao relógio. Arrasta o ícone para a barra de tarefas, ou vai a *Definições → Personalização → Barra de tarefas → Outros ícones da barra de tarefas* e ativa o **ClaudemonWin**.
3. Clica no ícone. Os números podem demorar até cerca de um minuto a aparecer da primeira vez.
4. Na **⚙** do painel ativa **Iniciar com o Windows** se quiseres que a app arranque sozinha.

---

## Utilização

| Ação | Como |
|---|---|
| Ver o uso | Clique esquerdo no ícone, ou passar o rato por cima para ver o resumo |
| Atualizar agora | Botão ⟳ no painel, **F5** com o painel aberto, ou clique direito no ícone → *Atualizar* |
| Definições | Botão ⚙ no painel |
| Modo escuro / claro | ⚙ → *Tema* → Sistema · Claro · Escuro |
| Janela flutuante | ⚙ → *Janela flutuante*, ou clique direito no ícone |
| Vista compacta | Duplo-clique na janela flutuante |
| Fechar o painel | **Esc**, ou clicar fora dele |
| Sair | *Sair* no rodapé do painel, ou clique direito no ícone → *Sair* |
| Ver logs e dados | Clique direito no ícone → *Abrir pasta de dados* |

---

## Resolução de problemas

### O script "não está assinado digitalmente"

```
.\build.ps1 : File ...\build.ps1 cannot be loaded. The file ... is not digitally signed.
You cannot run this script on the current system.
```

**Causa:** o Windows marca os ficheiros descarregados da internet (ZIP do GitHub, anexos) e a política de execução do PowerShell bloqueia scripts marcados que não estão assinados. O script em si não tem nenhum problema.

**Soluções** (basta uma):

```powershell
# 1) Correr o script só desta vez, sem alterar a política do sistema
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1

# 2) Remover a marca "veio da internet" de todos os ficheiros do projeto
Get-ChildItem -Recurse | Unblock-File
.\scripts\build.ps1
```

Se a política da empresa bloquear as duas, usa os [comandos manuais](#opção-b-compilar-a-partir-do-código) (`dotnet test` e `dotnet publish`), que não dependem de scripts. Clonar com `git clone` em vez de descarregar o ZIP também evita a marca.

### `NETSDK1135: SupportedOSPlatformVersion 10.0.17763.0 cannot be higher than TargetPlatformVersion 7.0`

**Causa:** acontecia com as primeiras versões do código. O ficheiro do projeto pedia uma versão mínima do Windows que não é compatível com o destino `net8.0-windows`. **Já está corrigido neste repositório.**

Se tiveres uma cópia antiga, apaga a linha com `SupportedOSPlatformVersion` em `src\ClaudemonWin\ClaudemonWin.csproj`, ou corre a partir da raiz do projeto:

```powershell
(Get-Content src\ClaudemonWin\ClaudemonWin.csproj) | Where-Object { $_ -notmatch 'SupportedOSPlatformVersion' } | Set-Content src\ClaudemonWin\ClaudemonWin.csproj
```

### `dotnet` não é reconhecido

O .NET 8 SDK não está instalado, ou o terminal foi aberto antes da instalação. Corre `winget install Microsoft.DotNet.SDK.8`, fecha e volta a abrir o PowerShell e confirma com `dotnet --list-sdks`, que deve mostrar `8.0.x`.

### A primeira compilação mostra "Welcome to .NET" e instala um certificado HTTPS

É normal. É a configuração inicial do SDK e só acontece uma vez. Para desligar a telemetria do .NET:

```powershell
setx DOTNET_CLI_TELEMETRY_OPTOUT 1
```

### "O Windows protegeu o seu PC" (SmartScreen)

O `.exe` não tem assinatura de código, por isso o SmartScreen avisa nas primeiras execuções. Clica em **Mais informações → Executar mesmo assim**. Se o `.exe` vier de um download, também podes clicar com o botão direito → *Propriedades* → marcar **Desbloquear** → *OK*.

### "To run this application, you must install .NET"

Estás a usar o `ClaudemonWin.exe` pequeno sem o runtime. Instala-o com `winget install Microsoft.DotNet.DesktopRuntime.8`, ou usa o `ClaudemonWin-standalone.exe`.

### O painel diz "O Claude Code não está instalado"

A app não encontrou o `claude`. Abre um terminal novo e corre:

```powershell
where.exe claude
claude --version
```

- Se não aparecer nada, instala o Claude Code (ver [Requisitos](#requisitos)).
- Se aparecer, confirma que o caminho está numa das pastas que a app verifica:
  - `%USERPROFILE%\.local\bin`
  - `%APPDATA%\npm`
  - WinGet
  - Scoop
  - pnpm
  - Volta
  - Bun
  - nvm-windows e fnm
  - qualquer pasta do `PATH` do utilizador

  Se instalaste o Claude Code com a app aberta, sai e volta a abrir a app.

### O painel diz "Inicia sessão no Claude Code"

Clica em **Abrir terminal com claude** no painel, faz `/login` com a tua subscrição e depois clica em ⟳.

### Fica em "A aguardar dados de uso…"

O CLI só atualiza os limites mais ou menos uma vez por minuto, por isso espera 1 a 2 minutos. Se ficar assim, confirma que o comando funciona fora da app:

```powershell
claude -p "/usage" --output-format json
```

O `result` deve conter linhas como `Current session: 28% used · resets ...`. Se o texto for diferente, abre uma *issue* com o resultado, sem dados pessoais. O log da app também ajuda (ver a secção seguinte).

### Ver o log

Clique direito no ícone → **Abrir pasta de dados** → `claudemon.log`. O log mostra o caminho do `claude` encontrado e todos os erros de leitura.

### O ícone não aparece

Está na zona escondida **`^`**. Vê o passo 2 de [Primeira utilização](#primeira-utilização).

### Não aparecem notificações

- Confirma que os alertas estão ligados na ⚙ do painel.
- Confirma que o Windows não está em *Não incomodar* / *Assistente de concentração*.
- Em *Definições → Sistema → Notificações*, confirma que as notificações do **ClaudemonWin** estão ativas.

### "Iniciar com o Windows" não funciona

Se desativaste a app em *Gestor de Tarefas → Aplicações de arranque*, liga e volta a ligar a opção na ⚙, porque isso limpa esse bloqueio. Se moveste o `.exe` para outra pasta, abre-o uma vez a partir da pasta nova e o arranque automático é atualizado.

---

## Desinstalar

1. Na ⚙ desliga **Iniciar com o Windows** e depois sai da app.
2. Apaga o `.exe`.
3. Opcional: apaga as pastas `%APPDATA%\ClaudemonWin` (definições) e `%LOCALAPPDATA%\ClaudemonWin` (cache, log e pasta de trabalho).

---

## Como funciona

A app corre o seguinte comando de 60 em 60 segundos, sem janela de consola:

```
claude -p "/usage" --output-format json
```

Corre numa pasta neutra (`%LOCALAPPDATA%\ClaudemonWin\work`) para o CLI nunca analisar os teus projetos. Depois lê o campo `.result` e extrai as linhas:

```
Current session: 28% used · resets Jun 24 at 2:49am (Europe/Istanbul)
Current week (all models): 29% used · resets Jun 26 at 9:59am (Europe/Istanbul)
Current week (Fable): 2% used · resets Jun 26 at 10am (Europe/Istanbul)
```

O parser é tolerante: aceita o formato em várias linhas, horas sem data e relógio de 24 horas, e deduz o ano da reposição.

**Comportamento perante falhas e mudanças de estado:**

- Se a resposta não trouxer as linhas de limites, a app mantém os últimos valores sem mostrar erro e volta a tentar ao fim de 28 s.
- Cada chamada tem um limite de 30 s. Se o `claude` não responder nesse tempo, a app termina o processo e tudo o que ele lançou.
- Ao suspender o PC a recolha pára, e retoma 8 s depois de acordar.
- Os alertas não se repetem na mesma janela de quota, mesmo que reinicies a app.

Para não arrancar atualizações nem telemetria em cada chamada, a app define estas variáveis só no processo que lança: `DISABLE_AUTOUPDATER=1` e `NO_COLOR=1`. As tuas sessões normais do `claude` não são afetadas.

| Ficheiro | Localização |
|---|---|
| Definições | `%APPDATA%\ClaudemonWin\settings.json` |
| Última leitura (cache) | `%LOCALAPPDATA%\ClaudemonWin\usage-cache.json` |
| Estado dos alertas | `%LOCALAPPDATA%\ClaudemonWin\alert-state.json` |
| Log | `%LOCALAPPDATA%\ClaudemonWin\claudemon.log` |

### Limitações conhecidas

- Os valores só são atualizados enquanto a app está aberta.
- O texto do `/usage` não é uma API oficial. Se a Anthropic mudar o formato, a app mostra um erro em vez de rebentar.
- Cada chamada pode deixar uma sessão no histórico do Claude Code, em `%USERPROFILE%\.claude\projects`, associada à pasta `work`. Se isso incomodar, aumenta `PollInterval` em `src/ClaudemonWin/UsageController.cs`.
- O `.exe` não tem assinatura de código, por isso o SmartScreen avisa nas primeiras execuções.

---

## Desenvolvimento

```
ClaudemonWin.sln
├─ src/ClaudemonWin.Core          net8.0          lógica pura: parser, alertas, máquina de estados,
│                                                 deteção do claude, execução do CLI
├─ src/ClaudemonWin               net8.0-windows  app WinForms: ícone, painel, janela flutuante,
│                                                 notificações, arranque automático
├─ tests/ClaudemonWin.Core.Tests  xUnit           57 testes
├─ scripts/build.ps1              testes + publish
└─ .github/workflows/build.yml    CI em Windows; anexa os .exe às releases
```

- Abre `ClaudemonWin.sln` no **Visual Studio 2022** (componente *.NET desktop development*), no **Rider** ou no **VS Code** com o C# Dev Kit.
- Para correr os testes: `dotnet test`.
- Para correr a app em modo debug: `dotnet run --project src/ClaudemonWin`.
- Os textos da interface (PT/EN) estão em `src/ClaudemonWin/Strings.cs` e as cores em `src/ClaudemonWin/Theme.cs`.

### Publicar uma nova versão

```powershell
git tag v0.1.0
git push origin v0.1.0
```

O workflow compila, testa e cria a release com `ClaudemonWin.exe` e `ClaudemonWin-standalone.exe` anexados.

---

## Privacidade

Tudo fica no teu PC. A app não faz pedidos de rede: só corre o `claude` local e lê o resultado. Não há telemetria.

## Licença

[MIT](LICENSE). Baseado no [Claudemon](https://github.com/ardabalkandev/claudemon) de Arda Balkan (MIT).
