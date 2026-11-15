# Emu Launcher

[![Baixar para Windows](https://img.shields.io/badge/Baixar_para_Windows-x64-A78BFA?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/sagaopc/EmuLauncher/releases/latest/download/EmuLauncher-windows-x64.zip)
[![Versão mais recente](https://img.shields.io/github/v/release/sagaopc/EmuLauncher?style=for-the-badge&label=vers%C3%A3o&color=1F1F24)](https://github.com/sagaopc/EmuLauncher/releases/latest)
[![Compilação](https://img.shields.io/github/actions/workflow/status/sagaopc/EmuLauncher/compilar.yml?branch=main&style=for-the-badge&label=compila%C3%A7%C3%A3o)](https://github.com/sagaopc/EmuLauncher/actions/workflows/compilar.yml)

Launcher de emuladores para usar no sofá, com controle, numa televisão: baixa os emuladores,
mantém atualizados e abre, tudo pelo mesmo lugar. É feito a partir do **Emulator Manager** (a busca
de versões, a escolha do arquivo e a atualização segura são o mesmo código) e usa o visual do
**Fritchii**: tema escuro "Graphite", um destaque violeta, fonte Inter, barra de ícones à esquerda e
um cartão grande para o emulador selecionado.

Não tem biblioteca de jogos: ele abre os próprios emuladores.

> Em teste. Relatos de problemas em [Issues](../../issues).

## Download

**[Baixar a versão mais recente](https://github.com/sagaopc/EmuLauncher/releases/latest/download/EmuLauncher-windows-x64.zip)**
(`EmuLauncher-windows-x64.zip`), descompactar e abrir o `EmuLauncher.exe`. Não precisa instalar
nada (Windows 10 e 11, 64 bits). As versões anteriores e as notas de cada uma ficam em
**[Releases](../../releases)**.

O `.exe` é compilado pelo GitHub Actions a partir deste código a cada versão
(`.github/workflows/compilar.yml`). Na primeira vez, o Windows pode mostrar "O Windows protegeu o
computador", porque o `.exe` não tem assinatura digital paga: clique em **Mais informações** e depois
em **Executar assim mesmo**.

## O que ele faz

- **Biblioteca** (tela inicial): o emulador selecionado num cartão grande com Jogar / Atualizar /
  Opções, e os outros numa fileira embaixo. O último que você jogou vem primeiro.
- **Baixar emuladores**: um catálogo pronto (lista abaixo). Escolha um, confirme o arquivo e ele é
  baixado, extraído na própria pasta e colocado na biblioteca com o executável certo.
- **Atualizar**: procura a versão mais nova e instala por cima da pasta. Um ponto violeta no ícone
  marca os emuladores com atualização esperando.
- **Jogar**: abre o emulador. Quando ele fecha, o launcher volta para a frente.
- **Adicionar outro repositório**: qualquer repositório do GitHub, GitLab ou Gitea/Forgejo, ou só um
  `.exe` que você já tem, sem repositório nenhum. É a única tela que precisa de teclado.
- Em português ou inglês, conforme o idioma do Windows.

Os emuladores que você já acompanha no **Emulator Manager** são importados na primeira vez que o
launcher abre (`%APPDATA%\EmulatorManager\emulators.json` só é lido, nunca alterado).

## Controles

| Controle | Teclado | Ação |
| --- | --- | --- |
| Direcional / analógico esquerdo | Setas | Mover |
| A | Enter, Espaço | Escolher. Num emulador: jogar (ou instalar) |
| B | Esc, Backspace | Voltar / cancelar |
| X | U, F5 | Atualizar o emulador selecionado |
| Y, Start | M, tecla Menu | Opções do emulador selecionado |
| LB / RB | Page Up / Page Down | Página anterior / próxima |
| | F11 | Tela cheia |

Com o mouse, um clique seleciona e dois cliques abrem.

O controle é lido por XInput: controles de Xbox e qualquer um que se apresente como um. DualSense,
DualShock e Pro Controller funcionam quando algo os converte para XInput (Steam Input, DS4Windows,
BetterJoy). Os comandos são ignorados enquanto o launcher não é a janela ativa, então jogar num
emulador não mexe em nada atrás dele.

A interface é desenhada para uma tela de 720 linhas e redimensionada para a janela, então tem as
mesmas proporções num monitor 1080p e numa TV 4K.

## Emuladores pré-definidos

**Switch 2**

| Nome | De onde vem |
| --- | --- |
| Lichii | releases de [zortos293/Lichii](https://github.com/zortos293/Lichii) (tags `build-…`) |
| Lichii · Actions | builds de teste de [zortos293/Lichii](https://github.com/zortos293/Lichii/actions), workflow `release-binaries` |
| Fritchii | releases de [Frizti/Lichii](https://github.com/Frizti/Lichii) (tags `fritchii-v…` e `frizti-v…`) |
| Lichii Sharkiix | [Shakoquo/Lichii-sharkiix-version](https://github.com/Shakoquo/Lichii-sharkiix-version) |
| Eglantine Community | [Eglantine-Community/Eglantine](https://github.com/Eglantine-Community/Eglantine) (daily) |
| Eglantine PokeDinner | [Franseven/PokeDinner-a-Eglantine-Fork](https://git.eden-emu.dev/Franseven/PokeDinner-a-Eglantine-Fork) |
| Selevet Eglantine | [sylvieistaken/selevet-eglantine](https://github.com/sylvieistaken/selevet-eglantine) |
| Eglantine SmileyTatsu | [SmileyTatsu/Eglantine](https://github.com/SmileyTatsu/Eglantine) (low end) |
| Eglantine A Man With A Plan | [A-Man-With-A-Plan-dev/Eglantine](https://github.com/A-Man-With-A-Plan-dev/Eglantine) (Zelda) |
| Eden nx2 | [Takoroka/eden-nx2](https://git.eden-emu.dev/Takoroka/eden-nx2) |
| Eden | [eden-emu/eden](https://git.eden-emu.dev/eden-emu/eden) |

**Outros consoles**: Cemu (Wii U), Azahar (3DS), melonDS (DS), mGBA (GBA), RMG (N64), Snes9x,
Mesen (NES, SNES, Game Boy), DuckStation (PS1), PCSX2 (PS2), RPCS3 (PS3), PPSSPP (PSP), Vita3K
(PS Vita), Flycast (Dreamcast), xemu (Xbox), Xenia Canary (Xbox 360) e ares (vários).

A lista fica em `Services/Catalog.cs`. Cada emulador vai para a própria pasta, então forks do mesmo
emulador convivem lado a lado.

DuckStation e PCSX2 abrem com `-bigpicture -fullscreen`, a interface para controle deles. Dá para
mudar em Opções > Editar > Argumentos ao iniciar.

Dolphin e RetroArch não estão no catálogo porque não publicam os builds como releases de
repositório. Adicione-os em "Adicionar outro repositório" só com o executável e o launcher abre os
dois (sem atualização).

### Acrescentar emuladores sem recompilar

Crie `%APPDATA%\EmuLauncher\catalog.json` (há um modelo em `catalog.exemplo.json`):

```json
[
  {
    "Id": "meu-emulador",
    "Name": "Meu Emulador",
    "Platform": "Algum console",
    "RepoUrl": "https://github.com/dono/repo",
    "TagFilter": "",
    "Exe": [ "meu-emulador.exe" ],
    "Arguments": ""
  }
]
```

Um item com o `Id` de um emulador pré-definido substitui esse emulador. `TagFilter` restringe a busca
às versões cuja tag contém aquele texto; separe alternativas com `|` (ex.: `fritchii-v|frizti-v`).
`Exe` lista o programa a abrir, o melhor primeiro, com curingas; se nenhum bater, é usado o maior
programa perto do topo da pasta.

## Repositórios e versões

O tipo de repositório é reconhecido pelo endereço: GitHub, GitLab ou, para qualquer outro, Gitea /
Forgejo (como o `git.eden-emu.dev`). Vale o **release publicado mais recente**, pré-releases
incluídos; repositórios sem releases usam a tag mais nova. Todos os arquivos do release são
oferecidos, com o melhor para Windows x64 em primeiro e marcado como recomendado.

- **Releases contínuos** (`latest`, `continuous`, `daily`) são comparados pela data de publicação,
  então uma atualização é percebida mesmo quando a tag não muda.
- Um release que é **um único `.exe` portátil** é instalado como está.

O GitHub permite 60 consultas anônimas por hora. Defina a variável de ambiente `GITHUB_TOKEN` para
aumentar o limite.

## Builds do GitHub Actions

Cole o link da aba **Actions** de um repositório em vez do próprio repositório e o launcher segue os
builds de lá em vez dos releases:

```
https://github.com/dono/repo/actions                         build mais novo de qualquer workflow
https://github.com/dono/repo/actions/workflows/build.yml     só daquele workflow
```

Funciona em "Adicionar outro repositório", em Opções > Editar e no `catalog.json`. O link normal do
repositório continua significando "releases".

- É usado o **build mais novo concluído com sucesso que ainda tem arquivos**. Execuções que não
  publicam nada (testes, etiquetar issues) são puladas. A versão aparece como
  `build-<número>-<commit>`.
- Builds de **pull requests e de forks nunca são oferecidos**: rodam código que não é dos donos do
  repositório.
- O build mais novo pode ser de qualquer branch, inclusive um em andamento. O diálogo mostra o
  workflow e o branch antes de baixar, e o **filtro de release** restringe: ele é comparado com o
  nome do workflow, o branch e o título do build (`main`, `nx2`...).
- O GitHub embrulha cada arquivo de build num zip próprio; quando esse zip só tem outro pacote
  dentro, o launcher abre esse também.
- O GitHub apaga os arquivos de build depois de um tempo (90 dias por padrão).

**Baixar exige login no GitHub, mesmo em repositório público.** Com `GITHUB_TOKEN` definido, o
arquivo vem direto do GitHub (um token fine-grained precisa de "Actions: read"; um clássico, do
escopo `repo`). Sem token, o launcher baixa pelo [nightly.link](https://nightly.link/), um serviço
de código aberto que entrega os mesmos arquivos de repositórios públicos sem login, e o diálogo avisa
quando é o caso. O token só é enviado para `api.github.com`.

## Atualização segura

1. O release é baixado numa pasta temporária; a instalação não é tocada.
2. É extraído numa pasta separada (com proteção contra caminhos maliciosos).
3. Só depois da extração completa os arquivos são copiados por cima da pasta de instalação.
4. Nada é apagado: configurações, saves e cartões de memória dentro da pasta do emulador sobrevivem
   à atualização, e uma atualização cancelada ou com erro deixa a instalação antiga no lugar.

Remover um emulador da biblioteca nunca apaga os arquivos dele.

## Compilar

Precisa do SDK do .NET 8 no Windows.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O resultado é um único executável:

```
bin\Release\net8.0-windows\win-x64\publish\EmuLauncher.exe
```

Para testar durante o desenvolvimento: `dotnet run`.

Para publicar uma versão: escreva as notas em `docs/notas/v<versão>.md` e rode
**Actions > Compilar > Run workflow** informando a versão.

## Onde ficam as coisas

```
%APPDATA%\EmuLauncher\emulators.json     a biblioteca
%APPDATA%\EmuLauncher\settings.json      os ajustes
%APPDATA%\EmuLauncher\catalog.json       emuladores extras do catálogo (opcional)
%LOCALAPPDATA%\EmuLauncher\Emulators\    pasta padrão dos emuladores (dá para mudar em Ajustes)
```

## Créditos e licença

- Busca de versões e atualização: Emulator Manager.
- Visual: a paleta, as formas e o estilo dos ícones seguem a interface "Graphite" do
  [Fritchii](https://github.com/Frizti/Lichii) (GPL-3.0-or-later). Por isso este projeto também é
  distribuído sob a **GPL-3.0-or-later** (veja `LICENSE`).
- Fonte Inter: SIL Open Font License 1.1 (`Fonts/OFL.txt`).
- Extração de pacotes: [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT).
- Downloads de builds do Actions sem login: [nightly.link](https://nightly.link/).
- Os emuladores pertencem aos seus autores; o launcher só baixa o que eles publicam.
