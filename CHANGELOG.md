# Changelog

## [0.11.0] - 2026-08-14

### Added

- Configurações agora oferecem um seletor nativo de cor de destaque. Uma única cor-base persistida gera automaticamente variantes acessíveis para destaque, superfícies suaves, hover e glow nos temas claro e escuro.
- A interface agora pode ser alternada entre `pt-BR` e `en-US` nas configurações, incluindo textos, tray, estados, tooltips, previsões, contagem regressiva e formatação numérica.
- O painel de configurações recebeu polimento de ritmo, contraste e alinhamento dos campos; o caminho manual do Codex agora só aparece como fallback após falhar a auto-detecção, enquanto o acesso ao log permanece disponível.

### Fixed

- O parse frio do histórico local agora processa arquivos JSONL independentes em paralelo limitado e lê cada arquivo somente até o tamanho capturado no planejamento, mantendo cache, deduplicação e resultados determinísticos; numa cópia estável do histórico local de 394 arquivos/532,8 MB, a mediana caiu de 5.002 ms para 3.007 ms (aprox. 40%).
- A abertura detalhada mantém quota oficial e analytics locais em paralelo, mas agora preserva o resultado que terminar primeiro: se analytics terminar antes do snapshot, ele é aplicado uma única vez quando a quota chega, sem releitura nem painel incompleto.
- A lista de agents permanece totalmente fechada no modo detalhado, inclusive quando um novo agent começa a trabalhar depois da troca de modo.
- Hover e ripple das linhas de agents/subagents agora ocupam toda a largura do wrapper, com respiro vertical ao redor do texto sem perder a indentação hierárquica.
- O glow de trabalho não aparece mais nas barras do ranking; ele permanece exclusivo do gauge semanal de quota.
- O seletor de período Dia/Semana/Mês do ranking agora sinaliza interação com cursor de mão no hover.
- Modelo e effort na lista de agents agora usam uma variante menos saturada, mas com contraste garantido, da cor de destaque escolhida.
- Labels do painel de configurações agora compartilham tipografia e cor semântica; checkboxes têm respiro maior e ComboBox preserva texto contrastante nos temas claro e escuro.
- O botão visível de Pin foi removido do chrome; a preferência Sempre no topo continua disponível e persistida exclusivamente nas configurações.
- Modelo e effort passaram para a mesma linha do tipo na lista de agents; o tempo de execução fica alinhado à direita na linha de status, com truncamento para títulos e metadados longos.
- O tema escuro agora usa `#2D2D2D` como superfície-base consistente; Settings ajusta automaticamente a altura ao conteúdo dentro da área de trabalho, bloqueia resize manual, amplia o respiro dos checkboxes e deixa ComboBox mais confortável e claramente interativo.

## [0.10.6] - 2026-08-14

### Fixed

- O chevron do indicador de agents volta a aparecer em todo hover, independente de a lista estar aberta ou fechada.
- Linhas de agents/subagents agora clareiam suavemente no hover e exibem um ripple de glow a partir do ponto exato do clique antes de abrir a conversa.
- O glow do reasoning passou a usar uma faixa física constante, com percurso de 1,30 s e espera de 2 s entre ciclos, preservando a velocidade visual em textos curtos e longos.
- A lista de agents fecha somente de forma visual ao entrar no modo detalhado e reabre no compacto quando a preferência persistida e agents ativos permitem.

## [0.10.5] - 2026-08-14

### Fixed

- O indicador de agents mantém o número visível fora do hover, mesmo com a lista aberta. No hover, ele troca para um chevron compacto, centralizado e proporcional: para baixo fechado e para cima aberto.
- Cada linha de agent/subagent agora abre a conversa correspondente no Codex por deep link validado, sem fechar a lista.

## [0.10.4] - 2026-08-14

### Fixed

- A lista de agents agora guarda sua preferência de expansão, não fecha ao clicar fora do widget e restaura-se quando novos agents surgem. O hover fechado mostra novamente a seta para baixo.

### Added

- A abertura automática da lista usa uma entrada curta ancorada ao indicador; com a lista já aberta, apenas o agent novo recebe uma entrada discreta. As animações respeitam a redução de movimento do Windows.

## [0.10.3] - 2026-08-14

### Fixed

- O glow do reasoning agora percorre o texto da esquerda para a direita; a lista de agents ficou sem contorno externo e o indicador compacto usa fundo escuro com texto e seta brancos.

## [0.10.2] - 2026-08-14

### Changed

- A lista de agents ganhou tipografia e espacamento mais legiveis, hierarquia pai-filho com recuo e glow cinza discreto no reasoning ativo, desativado quando o Windows reduz animacoes.

## [0.10.1] - 2026-08-14

### Fixed

- O indicador compacto agora centraliza a seta, mostra seta para cima enquanto a lista esta aberta e a fecha ao clicar; durante o arraste, a lista aberta acompanha o widget.
- O status dos agents em execucao agora mostra o raciocinio/atividade corrente do agente e nao e substituido por mensagens de commentary.

## [0.10.0] - 2026-08-14

### Added

- A visualizacao compacta agora mostra quantos agents e subagents estao trabalhando e abre uma lista com tipo, titulo, ultimo status, modelo, effort e tempo em execucao.
- O progresso verde recebe um glow reverso de um segundo, com dois segundos de intervalo, enquanto houver trabalho ativo do Codex.

## [0.9.1] - 2026-08-13

### Fixed

- A restauracao da posicao do widget agora reconhece todas as areas de trabalho dos monitores: posicoes em telas secundarias ou com coordenadas negativas sao preservadas, e uma posicao realmente fora das telas disponiveis volta de forma acessivel ao monitor mais proximo.

## [0.9.0] - 2026-08-13

### Added

- A posicao final do widget agora e salva ao terminar cada arraste e permanece em `%APPDATA%\\CodexTracker\\settings.json` entre atualizacoes e reinstalacoes.

### Changed

- A fonte do widget minimalista foi aumentada em mais 15%, para 15,18 DIP, preservando a escala proporcional durante o redimensionamento.

## [0.8.2] - 2026-08-13

### Changed

- A fonte do widget minimalista foi aumentada em 20%, preservando a escala proporcional durante o redimensionamento.

## [0.8.1] - 2026-08-13

### Fixed

- O risco de esgotamento agora aparece logo abaixo do contador de reset e recebe destaque amarelo em negrito somente quando a quota pode acabar antes do reset.
- O contador de reset nao exibe mais o texto redundante "Restante esta semana".
- A altura maxima da visualizacao detalhada acompanha o conteudo para evitar espaco vazio abaixo da versao.

## [0.8.0] - 2026-08-13

### Changed

- A distribuicao agora usa .NET Framework 4.8 fornecido pelo Windows 10 22H2 e Windows 11 suportados, em vez de embutir o runtime .NET 8; o instalador fica drasticamente menor sem download externo durante a instalacao.
- O instalador passa a exigir Windows 10 22H2 ou posterior, que inclui o .NET Framework 4.8.

## [0.7.0] - 2026-08-13

### Added

- O widget agora permanece exclusivamente na bandeja do sistema: nao aparece na barra de tarefas nem no seletor Alt+Tab, e o menu "Mostrar" continua a exibi-lo e ativa-lo.

## [0.6.4] - 2026-08-13

### Fixed

- A entrega local obrigat\u00f3ria agora tamb\u00e9m inicia o aplicativo instalado e verifica o caminho e a vers\u00e3o do processo em execu\u00e7\u00e3o.

## [0.6.3] - 2026-08-13

### Fixed

- O ranking semanal agora considera apenas o ciclo ativo de quota principal do Codex, sem fallback para a semana de calendario quando os limites nao estao disponiveis.

## [0.6.2] - 2026-08-13

### Fixed

- As regras de entrega local agora exigem declarar a versão-alvo, compilar, gerar e instalar o instalador e verificar a versão instalada.
- O texto de versão na visualização detalhada foi duplicado para leitura sem esforço.

## [0.6.1] - 2026-08-13

### Fixed

- O compacto agora limita a largura a 100 DIP, escala proporcionalmente o texto central e desativa a composicao nao-cliente do DWM para nao exibir sombra.
- As superficies detalhada e de configuracoes usam fundos opacos tematicos, preservando os cantos arredondados.

Todas as mudanças relevantes deste projeto são registradas neste arquivo, seguindo [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [0.6.0] - 2026-08-13

### Added

- GitHub Actions release automation builds the Windows installer, attaches it to the release, and retains only the newest release and tag.

## [0.5.0] - 2026-08-13

## [0.5.1] - 2026-08-13

### Fixed

- O widget compacto agora deixa transparente toda a area fora do fundo circular, sem recorte nativo que corte o antialias do indicador.

### Added

- Versão da aplicação exibida de forma discreta, centralizada no rodapé da visualização detalhada.
- Fonte única de versão em `VERSION`, usada pela build e pelo instalador.
- Regra de versionamento semântico para alterações futuras.

