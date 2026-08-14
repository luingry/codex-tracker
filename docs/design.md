# Design do Codex Tracker 0.4.7

## Direção visual

A interface usa superfícies sólidas inspiradas no Codex/OpenAI. O tema claro combina warm-white e neutros frios; o escuro usa charcoal e grafite. A cor de destaque aparece com parcimônia no gauge, barras e estados ativos; o usuário escolhe uma única cor-base nas configurações e o app deriva tons acessíveis para destaque, superfície suave, hover e glow em cada tema. A composição evita gradientes e ornamentação desnecessária; o glow funcional percorre o progresso enquanto há trabalho ativo.

O widget permanece sem moldura. O resize pelas quatro bordas é resolvido por hit-testing nativo, e os cantos seguem a preferência arredondada do Windows. Settings substitui visualmente o conteúdo normal por uma superfície opaca própria, permite alternar toda a interface entre `pt-BR` e `en-US` e oferece um color picker nativo com preview; Cancelar restaura idioma e paleta salvos, enquanto Aplicar persiste as escolhas.

## Modos

- Compacto: leitura semanal periférica, com gauge, `WEEKLY` e percentual restante. Quando há trabalho, um contador circular abaixo do gauge abre a lista de agents/subagents ativos com tipografia ampliada, status e metadados de execução. Fora do hover, ele preserva o número de agents; no hover, troca pelo chevron compacto (baixo fechado, cima aberto). Cada linha abre a conversa do agent no Codex por deep link validado, sem fechar a lista; o hover clareia toda a largura do wrapper em 400 ms, com respiro vertical, e o clique emite um glow radial de 600 ms a partir da posição do ponteiro. A preferência de lista aberta é persistida; clique fora não a fecha e, após uma pausa sem agents, ela volta a abrir quando há trabalho novo. No modo detalhado a lista permanece fechada mesmo quando agents surgem; no retorno ao compacto, reabre se houver agents ativos e a preferência ainda estiver ligada. A lista ordena os agentes em profundidade pai-filho, com recuo de 12 DIP por nível aplicado somente ao conteúdo, mantendo os estados interativos em largura total. A abertura do wrapper e a entrada de um novo item são breves e discretas, respeitando a preferência do Windows por reduzir animações.
- Detalhado: largura fixa de 300 px, conteúdo vertical rolável e controles discretos no hover.
- Settings: header e footer fixos; somente o corpo é rolável. Os grupos mantêm ritmo vertical uniforme, campos legíveis nos dois temas e chevron óptico no seletor. O controle de destaque separa visualmente amostra, rótulo e hexadecimal; o caminho manual do Codex permanece colapsado e só é exibido como fallback da auto-detecção, enquanto Abrir log fica sempre acessível.

O duplo clique sobre superfícies de leitura alterna compacto/detalhado. No modo detalhado isso inclui gauge, textos, gráfico e espaços vazios do conteúdo rolável. Settings e controles interativos — botões, toggles, seletores, campos e barras de rolagem — ficam fora do atalho para preservar foco e gestos próprios.

## Hierarquia de dados

O topo detalhado mantém a quota oficial semanal: percentual restante e tempo até o reset. Abaixo, uma linha única apresenta os analytics locais como `HOJE / SEMANA / MÊS` em `pt-BR` ou `TODAY / WEEK / MONTH` em `en-US`.

- `HOJE`: tokens locais do dia e custo equivalente API.
- `SEMANA`: tokens e custo equivalente API no intervalo exato entre `reset - 7d` e `reset`.
- `MÊS`: tokens locais do mês corrente e custo equivalente API.

O ranking de modelos mantém barras relativas. Dia, Semana e Mês usam cursor de mão para sinalizar o seletor interativo. O gráfico diário do mês apresenta uma barra por dia, normalizada pelo maior valor da série; dias sem uso permanecem discretos e o estado totalmente vazio não inventa atividade. O hover de cada coluna mostra dia, tokens abreviados e custo estimado em BRL ou USD conforme a moeda selecionada.

Quota e analytics têm fontes e garantias diferentes. A quota vem do `codex app-server` e corresponde ao limite oficial reportado. Tokens, custos, série diária e ranking são reconstruídos dos rollouts locais; custos são equivalentes API estimados, nunca cobrança real. O modelo e effort da lista de agents usam uma variante menos saturada da cor de destaque, derivada com contraste mínimo preservado nos temas claro e escuro, sem depender de opacidade. O reasoning corrente da lista usa texto cinza e um brilho cinza de faixa física constante de 64 DIP: ele atravessa apenas os glifos em 1,30 s e espera 2 s antes do próximo ciclo; quando o Windows reduz animações, o texto permanece cinza estático. O glow verde/colorido de trabalho pertence somente ao gauge semanal; barras de ranking permanecem estáticas.

## Upgrade e instância única

A aplicação permite uma única instância por caminho de executável. Uma segunda inicialização termina sem abrir outra janela. Para upgrade seguro, o instalador fecha automaticamente a instância em execução via Restart Manager e filtro de aplicações antes de substituir os binários; o desinstalador usa `--shutdown-existing`. Não é necessário encerrar o app manualmente. O instalador por usuário preserva `%APPDATA%\CodexTracker\settings.json`, mantendo posição, tamanho, modo, topmost, tema, idioma, cor de destaque, moeda e caminho configurado do Codex.
