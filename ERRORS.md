# Erros e solucoes conhecidas

## `WS_THICKFRAME` reservava uma faixa não-cliente no widget sem moldura

- **Sintoma:** o compacto exibia uma faixa horizontal no topo mesmo sem `WindowChrome`; ajustes de brush e borda DWM apenas mascaravam a cor.
- **Causa:** `ResizeMode=CanResize` mantém `WS_THICKFRAME`. Mesmo quando `WM_NCCALCSIZE` estende o client a todo o HWND (insets 0/0/0/0), o DWM ainda pode compor pixels de frame diferentes entre os estados ativo e inativo. Um resize apenas atualizava temporariamente esses pixels.
- **Solução:** usar `ResizeMode=NoResize`, que remove `WS_THICKFRAME` por construção, e implementar o resize no preview de mouse do WPF. O compacto mantém proporção 62:52 e limites 62–320 px; detalhado e Settings mantêm largura 300 px e resize apenas vertical. Captura perdida ou desativação encerra o gesto com segurança. Para multimonitor, o gesto captura a work area Win32 do monitor que continha os bounds iniciais, converte-a para DIP uma vez e a reutiliza até o fim; a geometria limita o tamanho na direção da alça em vez de aplicar clamp em `Left`/`Top`, preservando a borda oposta.
- **Prevenção:** diagnosticar janelas customizadas comparando style nativo, `GetWindowRect`, `GetClientRect`, `ClientToScreen` e pixels ativo/inativo. Não considerar inset zero prova de que o DWM deixou de compor o frame; se a aparência não-cliente precisa ser invariável, remova o style que a cria e substitua também sua interação. No resize manual, mantenha o gesto de borda reservado até o `MouseUp`, mesmo se a captura for perdida, para o mesmo clique nunca ser reinterpretado como arraste da janela. Nunca use `SystemParameters.WorkArea` durante um gesto multimonitor: ele representa só a área primária e pode teletransportar o widget.

## Gráfico WPF desenhado manualmente não recebia hover entre as barras

- **Sintoma:** o gráfico diário renderizava normalmente, mas mover o cursor sobre dias com barra mínima ou sobre o espaço vertical acima da barra não exibia o tooltip.
- **Causa:** um `FrameworkElement` sem `Background` participa do hit-test apenas nas primitivas efetivamente desenhadas. Como as barras ocupavam poucos pixels, a maior parte da coluna visual não gerava `MouseMove`.
- **Solução:** o controle passou a declarar uma superfície de hit-test própria e sobrescrever `HitTestCore`; cada dia mantém um retângulo de coluna exato, independente da altura visível da barra. O tooltip usa esse mapa para mostrar dia, tokens e custo na moeda selecionada.
- **Prevenção:** controles WPF renderizados diretamente em `OnRender` devem definir explicitamente sua geometria de hit-test. Não presuma que toda a caixa de layout recebe mouse apenas porque `ActualWidth` e `ActualHeight` estão definidos.

## `AccessViolationException` ao consultar composição do DWM

- **Sintoma:** a janela encerrava na inicialização ao aplicar o backdrop, com `System.AccessViolationException` em `DwmIsCompositionEnabled`.
- **Causa:** a função nativa retorna um `HRESULT` e recebe um ponteiro de saída para `BOOL`; declará-la como retorno booleano sem parâmetro corrompe a chamada nativa.
- **Solução:** declarar `DwmIsCompositionEnabled(out bool enabled)` com retorno `int` e só aplicar o backdrop quando o `HRESULT` indicar sucesso e a composição estiver ativa.
- **Prevenção:** conferir assinaturas P/Invoke com o contrato Win32, especialmente parâmetros de saída e tipos de retorno `HRESULT`.

## Desktop Acrylic fica sólido quando a janela perde ativação

- **Sintoma:** `DWMSBT_TRANSIENTWINDOW` exibe Desktop Acrylic com a janela ativa, mas a superfície fica sólida ao ativar outro aplicativo.
- **Causa:** Background/Desktop Acrylic substitui a translucidez por fallback sólido quando a janela desktop é desativada; esse comportamento é intencional da Microsoft.
- **Solução:** manter `CompositionTarget` transparente e o caminho DWM estável, aceitando o fallback sólido inativo. Glass persistente inativo não foi resolvido: `DesktopAcrylicController` com target `Windows.UI.Composition` caiu depois em `CoreMessaging` (`0xc000027b`), e o target `Microsoft.UI.Composition` exigido não está exposto na projeção C# usada.
- **Prevenção:** não misturar stacks `Windows.UI.Composition` e `Microsoft.UI.Composition`; não reportar transparência inativa como resolvida sem teste ativo/inativo. Uma solução futura exigiria interop Microsoft-only suportado, possivelmente via helper C++/WinRT.

## `WindowChrome` deixava faixa sólida no topo

- **Sintoma:** uma faixa horizontal sólida permanecia acima do conteúdo glass.
- **Causa:** a área de frame/caption do `WindowChrome` ainda era desenhada pelo sistema.
- **Solução:** definir `GlassFrameThickness=0` e desativar captions Aero, deixando o conteúdo controlar toda a superfície.
- **Prevenção:** em janelas WPF sem moldura, configure explicitamente frame, caption e hit testing; transparência apenas no conteúdo não elimina o frame nativo.

## Build Release falha porque o executável está em uso

- **Sintoma:** `MSB3027`/`MSB3021` ao copiar `apphost.exe` para `CodexTracker.exe` após várias tentativas.
- **Causa:** uma instância da própria build Release permaneceu aberta durante o smoke visual e manteve o executável bloqueado.
- **Solução:** encerrar somente a instância de teste identificada pelo caminho `src\CodexTracker\bin\Release` e repetir o build.
- **Prevenção:** finalizar o smoke local antes de recompilar ou gerar o instalador; não encerrar a instalação do usuário por nome de processo sem conferir o caminho.

## Tokens locais divergiam do total processado do Codex

- **Sintoma:** agosto aparecia abaixo do total auditado, apesar de todos os rollouts ativos parecerem presentes.
- **Causa:** o leitor ignorava `archived_sessions`, descartava o primeiro snapshot de forks e usava `total_tokens`. A auditoria do Codex soma 180 rollouts ativos e 14 arquivados; o total processado é `input_tokens + output_tokens + reasoning_output_tokens`. `cached_input_tokens` já é subconjunto da entrada. Além disso, um subagent pode incluir metadata herdado do pai depois de seu próprio `session_meta`; somente o primeiro metadata identifica o arquivo.
- **Solução:** a leitura padrão inclui `sessions` e `archived_sessions`. O primeiro snapshot cumulativo de cada rollout conta como contexto processado, snapshots seguintes entram por delta e uma queda inicia novo segmento. A métrica soma entrada, saída e reasoning; custo cobra reasoning como saída. Duplicatas só são removidas quando mesmo id e prefixo físico byte a byte comprovam checkpoint sobreposto.
- **Prevenção:** regressões cobrem as duas raízes padrão, fork com metadata herdado, primeiro snapshot de fork, reset, `total_tokens` divergente dos componentes e segmentos com mesmo id sem prefixo. Não usar quota, clamp ou igualdade de `session_id` como aproximação.

## `ffmpeg` sem decoder SVG ao exportar assets da marca

- **Sintoma:** `Decoding requested, but no decoder found for: svg` ao tentar converter o SVG fonte em PNG/ICO.
- **Causa:** a build local do `ffmpeg` reconhece o demuxer, mas nao inclui um renderer/decoder SVG como `librsvg`.
- **Solucao:** `scripts\export-brand-assets.ps1` desenha a mesma geometria com `System.Drawing` e empacota PNGs com alpha em um ICO multi-resolucao; cada `byte[]` e preservado como uma entrada, sem flattening do pipeline PowerShell.
- **Prevencao:** use o exportador do repositorio; nao presuma suporte SVG apenas porque `ffmpeg` esta instalado.

## `JsonElement` null em `secondary` ou `individualLimit`

- **Sintoma:** a leitura de `account/rateLimits/read` falhava porque `TryGetProperty` exige um objeto.
- **Causa:** o protocolo pode retornar `rateLimits.secondary=null` e `individualLimit=null`.
- **Solucao:** o parser valida `JsonValueKind.Object` antes de ler uma janela; limites ausentes nao viram 0%.
- **Prevencao:** o teste de regressao reproduz o payload com ambos os campos nulos.

## `Access is denied` ao chamar `codex` em WindowsApps

- **Sintoma:** a descoberta pelo `PATH` pode encontrar um alias em `WindowsApps` que nao e executavel por processos externos.
- **Causa:** App Execution Alias do Windows, em vez do binario real do Codex CLI.
- **Solucao:** o aplicativo tenta `where codex`, caminhos de instalacao conhecidos e um caminho configurado pelo usuario. Neste host, o binario funcional esta em `C:\Users\luing\.codex\plugins\.plugin-appserver\codex.exe`.
- **Prevencao:** configure explicitamente `CodexPath` em `%APPDATA%\CodexTracker\settings.json` quando a descoberta falhar.

## Inno Setup instalado pelo `winget`, mas `ISCC.exe` nao encontrado em `Program Files (x86)`

- **Sintoma:** a publicacao do instalador termina com `Inno Setup was installed but ISCC.exe was not found` depois de o `winget` confirmar a instalacao.
- **Causa:** o `winget` pode instalar Inno Setup por usuario em `%LOCALAPPDATA%\Programs\Inno Setup 6`, e nao no caminho tradicional em `Program Files (x86)`.
- **Solucao:** `scripts\build-installer.ps1` consulta `PATH`, o registro de desinstalacao e os caminhos por maquina e por usuario antes de compilar.
- **Prevencao:** use o script de build, em vez de fixar um caminho para `ISCC.exe`.

## Tema WPF falha ao alterar uma `SolidColorBrush`

- **Sintoma:** a janela encerrava na inicialização com `não é possível definir uma propriedade ... estado somente leitura`.
- **Causa:** brushes compartilhadas de `Application.Resources` podem ser congeladas pelo WPF.
- **Solução:** o gerenciador de tema substitui a brush inteira no dicionário de recursos; as superfícies visuais usam `DynamicResource` para recebê-la em tempo real.
- **Prevenção:** não mutar a propriedade `Color` de brushes declaradas em XAML.

## Quota principal parecia invertida em relação ao Codex

- **Sintoma:** o widget mostrava 16% quando o cliente Codex mostrava 84%.
- **Causa:** `account/rateLimits/read` expõe `usedPercent`; o cliente Codex apresenta o percentual restante. O parser preservava corretamente o valor de uso, mas a métrica periférica o mostrava diretamente.
- **Solução:** a apresentação semanal exibe `100 - usedPercent`; forecast continua recebendo o `usedPercent` original.
- **Prevenção:** teste de regressão com payload real-shaped (`usedPercent=16` resulta em `84%` exibido).

## Arraste da janela sem moldura nao iniciava sobre o conteudo

- **Sintoma:** clicar e arrastar o gauge, textos ou a area vazia do widget podia nao mover a janela.
- **Causa:** o arraste dependia de `MouseLeftButtonDown` com bubbling no `Grid` raiz e de `DragMove()`. Com `WindowChrome` e elementos sobrepostos, esse ponto de entrada nao e confiavel; alem disso, o chrome invisivel continuava hit-testable.
- **Solucao:** a janela observa o gesto no preview e só inicia o movimento nativo depois do limiar de arraste do Windows. Mantém a ativação normal, não reaplica o backdrop durante/depois do movimento e preserva cliques e duplos cliques.
- **Prevencao:** em janelas WPF sem moldura, diferencie clique de arraste pelo limiar nativo; não suprima ativação nem recomponha o backdrop como efeito colateral do gesto.

## Forecast semanal podia contradizer o risco e perder o timing

- **Sintoma:** a previsao podia exibir `Risco de esgotar antes do reset · 100% projetado`; notificacoes parciais tambem podiam apagar `resetsAt` e `windowDurationMins`, deixando a previsao indisponivel.
- **Causa:** o status comparava o valor bruto com 100%, enquanto a interface arredondava para inteiro. O merge sparse substituia a janela completa mesmo quando a notificacao trazia apenas o percentual usado, e o forecast era recalculado com o relogio atual em vez do instante do snapshot.
- **Solucao:** status e formatacao agora compartilham precisao de uma casa perto do limiar; 100% usado tem estado explicito. O forecast usa `ReceivedAt`, valida dados temporais e numericos, e o merge so preserva timing ausente quando percentual monotono, reset futuro e campos fornecidos comprovam o mesmo ciclo.
- **Prevencao:** regressões cobrem projecao e esgotamento exatos, limites temporais, offsets UTC, arredondamento e updates sparse do mesmo ciclo, de ciclo novo e apos o reset.

## Upgrade falhava ao substituir `clrjit.dll` com o app na bandeja

- **Sintoma:** o instalador abortava com `DeleteFile failed; code 5. Acesso negado` ao atualizar uma instalacao cujo Codex Tracker continuava aberto, inclusive oculto na bandeja.
- **Causa:** o Restart Manager registrava todos os arquivos do runtime self-contained (462 no repro), incluia `System` junto das instancias do app e recusava o fechamento com `Permission Denied + Session Mismatch`. Duas instancias instaladas podiam coexistir e manter `clrjit.dll` carregado. O desinstalador tambem nao encerrava automaticamente o processo antes de remover os arquivos.
- **Solucao:** `CloseApplicationsFilter` limita o Restart Manager ao executavel exato `CodexTracker.exe`; o app usa mutex por caminho instalado para impedir duplicatas futuras. Um evento nomeado acionado por `--shutdown-existing` permite ao desinstalador solicitar shutdown gracioso e aguardar a liberacao do mutex antes da remocao.
- **Prevencao:** `scripts/test-installer-upgrade.ps1` cobre instalacao, duas instancias legadas, upgrade com app aberto, single-instance na nova versao, relancamento, uninstall com app aberto, preservacao das configuracoes e ausencia de processos/arquivos orfaos.
