# Design do Codex Tracker 0.4.7

## Direção visual

A interface usa superfícies sólidas inspiradas no Codex/OpenAI. O tema claro combina warm-white e neutros frios; o escuro usa charcoal e grafite. O verde aparece com parcimônia no gauge, barras e estados positivos. A composição evita gradientes, glow e ornamentação desnecessária.

O widget permanece sem moldura. O resize pelas quatro bordas é resolvido por hit-testing nativo, e os cantos seguem a preferência arredondada do Windows. Settings substitui visualmente o conteúdo normal por uma superfície opaca própria.

## Modos

- Compacto: leitura semanal periférica, com gauge, `WEEKLY` e percentual restante.
- Detalhado: largura fixa de 300 px, conteúdo vertical rolável e controles discretos no hover.
- Settings: header e footer fixos; somente o corpo é rolável.

O duplo clique sobre superfícies de leitura alterna compacto/detalhado. No modo detalhado isso inclui gauge, textos, gráfico e espaços vazios do conteúdo rolável. Settings e controles interativos — botões, toggles, seletores, campos e barras de rolagem — ficam fora do atalho para preservar foco e gestos próprios.

## Hierarquia de dados

O topo detalhado mantém a quota oficial semanal: percentual restante e tempo até o reset. Abaixo, uma linha única apresenta os analytics locais em `HOJE / SEMANA / MÊS`.

- `HOJE`: tokens locais do dia e custo equivalente API.
- `SEMANA`: tokens e custo equivalente API no intervalo exato entre `reset - 7d` e `reset`.
- `MÊS`: tokens locais do mês corrente e custo equivalente API.

O ranking de modelos mantém barras relativas. O gráfico diário do mês apresenta uma barra por dia, normalizada pelo maior valor da série; dias sem uso permanecem discretos e o estado totalmente vazio não inventa atividade. O hover de cada coluna mostra dia, tokens abreviados e custo estimado em BRL ou USD conforme a moeda selecionada.

Quota e analytics têm fontes e garantias diferentes. A quota vem do `codex app-server` e corresponde ao limite oficial reportado. Tokens, custos, série diária e ranking são reconstruídos dos rollouts locais; custos são equivalentes API estimados, nunca cobrança real.

## Upgrade e instância única

A aplicação permite uma única instância por caminho de executável. Uma segunda inicialização termina sem abrir outra janela. Para upgrade seguro, o instalador fecha automaticamente a instância em execução via Restart Manager e filtro de aplicações antes de substituir os binários; o desinstalador usa `--shutdown-existing`. Não é necessário encerrar o app manualmente. O instalador por usuário preserva `%APPDATA%\CodexTracker\settings.json`, mantendo posição, tamanho, modo, topmost, tema, moeda e caminho configurado do Codex.
