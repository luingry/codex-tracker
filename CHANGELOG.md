# Changelog

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

