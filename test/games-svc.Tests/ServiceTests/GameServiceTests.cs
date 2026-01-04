using Application.DTO.GameDTO;
using Application.Services;
using AutoFixture;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Search;
using Domain.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Moq;

namespace games_svc.Tests.ServiceTests
{
    public class GameServiceTests : BaseTests
    {
        private List<Game> _stubList = null!;
        private Mock<IGameRepository> _mockRepo = null!;
        private Mock<IPurchaseRepository> _mockPurchaseRepo = null!;
        private Mock<IEventRepository> _mockEventRepo = null!;
        private Mock<IGameSearchProvider> _mockSearchProvider = null!;
        private Mock<IConfiguration> _mockConfiguration = null!;
        private IGameService _service = null!;

        protected override void InitStubs()
        {
            _stubList = _fixture.Build<Game>()
                                 .With(e => e._id, ObjectId.GenerateNewId())
                                 .CreateMany(2)
                                 .ToList();
        }

        protected override void MockDependencies()
        {
            _mockRepo = new Mock<IGameRepository>(MockBehavior.Strict);
            _mockPurchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);
            _mockEventRepo = new Mock<IEventRepository>(MockBehavior.Strict);
            _mockSearchProvider = new Mock<IGameSearchProvider>(MockBehavior.Loose);
            _mockConfiguration = new Mock<IConfiguration>();

            // IEventRepository setups - permitir qualquer AppendEventAsync
            _mockEventRepo
                .Setup(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // IGameRepository setups
            _mockRepo.Setup(r => r.GetAllAsync<ProjectGameDTO>())
                .ReturnsAsync(_stubList!.Select(x => new ProjectGameDTO(x)).ToList());

            _mockRepo.Setup(r => r.GetByIdAsync<ProjectGameDTO>(It.IsAny<ObjectId>()))
                .ReturnsAsync((ObjectId id) =>
                {
                    var game = _stubList?.FirstOrDefault(x => x._id == id);
                    return game == null ? null : new ProjectGameDTO(game);
                });

            _mockRepo.Setup(r => r.CreateAsync(It.IsAny<CreateGameDTO>()))
                .ReturnsAsync((CreateGameDTO dto) =>
                {
                    var entity = dto.ToEntity();
                    entity._id = ObjectId.GenerateNewId();
                    _stubList!.Add(entity);
                    return entity;
                });

            _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<ObjectId>(), It.IsAny<UpdateGameDTO>()))
                .Returns(Task.CompletedTask);

            _mockRepo.Setup(r => r.DeleteAsync(It.IsAny<ObjectId>()))
                .Callback<ObjectId>(id =>
                {
                    var index = _stubList!.FindIndex(x => x._id == id);
                    if (index >= 0) _stubList!.RemoveAt(index);
                })
                .Returns(Task.CompletedTask);

            _mockRepo.Setup(r => r.FindAsync<ProjectGameDTO>(It.IsAny<FilterGameDTO>()))
                .ReturnsAsync((FilterGameDTO filter) =>
                    _stubList!.Select(x => new ProjectGameDTO(x)).ToList());

            // IGameSearchProvider setups
            _mockSearchProvider.Setup(s => s.SearchAsync(It.IsAny<SearchGameDTO>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProjectGameSearchDTO>
                {
                    new ProjectGameSearchDTO { Id = ObjectId.GenerateNewId().ToString(), Name = "Game1", Category = "A", Price = 10, Score = 0.9 }
                });

            _mockSearchProvider.Setup(s => s.RecommendAsync(
                    It.IsAny<IReadOnlyCollection<ObjectId>>(),
                    It.IsAny<IReadOnlyCollection<ObjectId>>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<ObjectId> like, IReadOnlyCollection<ObjectId> exclude, int limit, CancellationToken ct) =>
                    _stubList!.Where(x => !exclude.Contains(x._id)).Take(limit).Select(x => new ProjectGameDTO(x)).ToList());

            _mockSearchProvider.Setup(s => s.UpsertAsync(It.IsAny<ProjectGameDTO>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _mockSearchProvider.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _mockSearchProvider.Setup(s => s.IsEnabled).Returns(true);

            // IPurchaseRepository setups
            _mockPurchaseRepo.Setup(r => r.GetTopPopularAsync(It.IsAny<int>()))
                .ReturnsAsync((int limit) =>
                    _stubList!.Take(limit).Select(x => new ProjectGameDTO(x)).ToList());

            _mockPurchaseRepo.Setup(r => r.GetUserPaidGameIdsAsync(It.IsAny<ObjectId>(), It.IsAny<int>()))
                .ReturnsAsync((ObjectId userId, int max) => new List<ObjectId>());

            // Instancia o serviço com todos os repositórios e providers
            _service = new GameService(
                _mockRepo.Object,
                _mockPurchaseRepo.Object,
                _mockEventRepo.Object,
                _mockSearchProvider.Object,
                _mockConfiguration.Object);
        }

        [Fact(DisplayName = "Deve retornar todos os jogos")]
        public async Task GetAllAsync_ReturnsEntities()
        {
            var result = await _service!.GetAllAsync();

            Assert.NotNull(result);
            Assert.Equal(_stubList!.Count, result.Count);
        }

        [Fact(DisplayName = "Deve retornar o jogo pelo Id")]
        public async Task GetByIdAsync_ReturnsEntity()
        {
            var item = _fixture.Build<Game>()
                   .With(e => e._id, ObjectId.GenerateNewId)
                   .Create();
            _stubList.Add(item);

            var result = await _service!.GetByIdAsync(item._id);

            Assert.NotNull(result);
            Assert.Equal(item._id, result!._id);
        }

        [Fact(DisplayName = "Deve criar um jogo e retornar o resultado esperado")]
        public async Task CreateAsync_CallsRepository_AndReturnsExpectedResult()
        {
            var dto = new CreateGameDTO
            {
                Name = "Test",
                Description = "Description test",
                Category = "FPS",
                ReleaseDate = DateTime.Now.AddMonths(-1),
                LastUpdateDate = DateTime.Now,
                Price = 59.99m
            };

            var response = await _service.CreateAsync(dto);

            Assert.False(response.HasError);
            Assert.Equal(201, response.StatusCode);
            Assert.NotNull(response.Data);

            _mockRepo.Verify(r => r.CreateAsync(dto), Times.Once);
            _mockRepo.Verify(r => r.GetByIdAsync<ProjectGameDTO>(response.Data._id), Times.Once);
            _mockEventRepo.Verify(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact(DisplayName = "Deve atualizar um jogo chamando o repositório")]
        public async Task UpdateAsync_CallsRepository()
        {
            var updateDto = _fixture.Build<UpdateGameDTO>().Create();

            await _service!.UpdateAsync(ObjectId.Empty, updateDto);

            _mockRepo!.Verify(r => r.UpdateAsync(ObjectId.Empty, updateDto), Times.Once);
            _mockEventRepo.Verify(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact(DisplayName = "Deve remover um jogo chamando o repositório")]
        public async Task DeleteAsync_CallsRepository()
        {
            await _service!.DeleteAsync(ObjectId.Empty);

            _mockRepo!.Verify(r => r.DeleteAsync(ObjectId.Empty), Times.Once);
            _mockEventRepo.Verify(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact(DisplayName = "Deve buscar jogos com SearchAsync usando o search provider")]
        public async Task SearchAsync_DeveRetornarResultados()
        {
            // Arrange
            var mockPurchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);
            var mockGameRepo = new Mock<IGameRepository>(MockBehavior.Strict);
            var mockEventRepo = new Mock<IEventRepository>(MockBehavior.Strict);
            var mockSearchProvider = new Mock<IGameSearchProvider>(MockBehavior.Loose);
            var mockConfiguration = new Mock<IConfiguration>();

            mockEventRepo
                .Setup(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var searchResult = new List<ProjectGameSearchDTO>
            {
                new() { Id = "1", Name = "Jogo 1", Category = "Ação", Price = 10, Score = 0.9 }
            };
            mockSearchProvider.Setup(s => s.SearchAsync(It.IsAny<SearchGameDTO>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);

            var service = new GameService(
                mockGameRepo.Object,
                mockPurchaseRepo.Object,
                mockEventRepo.Object,
                mockSearchProvider.Object,
                mockConfiguration.Object);

            // Act
            var result = await service.SearchAsync(new SearchGameDTO { Q = "Jogo" });

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Jogo 1", result[0].Name);
            mockSearchProvider.Verify(s => s.SearchAsync(It.IsAny<SearchGameDTO>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "Deve retornar jogos populares com GetPopularAsync")]
        public async Task GetPopularAsync_DeveRetornarPopulares()
        {
            // Arrange
            var mockPurchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);
            var mockGameRepo = new Mock<IGameRepository>(MockBehavior.Strict);
            var mockEventRepo = new Mock<IEventRepository>(MockBehavior.Strict);
            var mockSearchProvider = new Mock<IGameSearchProvider>(MockBehavior.Loose);
            var mockConfiguration = new Mock<IConfiguration>();

            mockEventRepo
                .Setup(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var popularGames = new List<ProjectGameDTO>
            {
                new ProjectGameDTO { _id = ObjectId.GenerateNewId(), Name = "Popular", Category = "Ação", Price = 99 }
            };
            mockPurchaseRepo.Setup(r => r.GetTopPopularAsync(It.IsAny<int>()))
                .ReturnsAsync(popularGames);

            var service = new GameService(
                mockGameRepo.Object,
                mockPurchaseRepo.Object,
                mockEventRepo.Object,
                mockSearchProvider.Object,
                mockConfiguration.Object);

            // Act
            var result = await service.GetPopularAsync(1);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Popular", result[0].Name);
        }

        [Fact(DisplayName = "Deve recomendar jogos populares quando usuário não tem histórico")]
        public async Task GetRecommendationsAsync_SemHistorico_DeveRetornarPopulares()
        {
            // Arrange
            var mockPurchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);
            var mockGameRepo = new Mock<IGameRepository>(MockBehavior.Strict);
            var mockEventRepo = new Mock<IEventRepository>(MockBehavior.Strict);
            var mockSearchProvider = new Mock<IGameSearchProvider>(MockBehavior.Loose);
            var mockConfiguration = new Mock<IConfiguration>();

            mockEventRepo
                .Setup(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var userId = ObjectId.GenerateNewId();
            var popularGames = new List<ProjectGameDTO>
            {
                new ProjectGameDTO { _id = ObjectId.GenerateNewId(), Name = "Popular", Category = "Ação", Price = 99 }
            };
            mockPurchaseRepo.Setup(r => r.GetUserPaidGameIdsAsync(userId, 10))
                .ReturnsAsync(new List<ObjectId>());
            mockPurchaseRepo.Setup(r => r.GetTopPopularAsync(It.IsAny<int>()))
                .ReturnsAsync(popularGames);

            var service = new GameService(
                mockGameRepo.Object,
                mockPurchaseRepo.Object,
                mockEventRepo.Object,
                mockSearchProvider.Object,
                mockConfiguration.Object);

            // Act
            var result = await service.GetRecommendationsAsync(userId, 1);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Popular", result[0].Name);
        }

        [Fact(DisplayName = "Deve recomendar jogos similares quando usuário tem histórico")]
        public async Task GetRecommendationsAsync_ComHistorico_DeveRetornarRecomendados()
        {
            // Arrange
            var mockPurchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);
            var mockGameRepo = new Mock<IGameRepository>(MockBehavior.Strict);
            var mockEventRepo = new Mock<IEventRepository>(MockBehavior.Strict);
            var mockSearchProvider = new Mock<IGameSearchProvider>(MockBehavior.Loose);
            var mockConfiguration = new Mock<IConfiguration>();

            mockEventRepo
                .Setup(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var userId = ObjectId.GenerateNewId();
            var purchasedIds = new List<ObjectId> { ObjectId.GenerateNewId() };
            var recommendedGames = new List<ProjectGameDTO>
            {
                new ProjectGameDTO { _id = ObjectId.GenerateNewId(), Name = "Recomendado", Category = "Ação", Price = 99 }
            };
            mockPurchaseRepo.Setup(r => r.GetUserPaidGameIdsAsync(userId, 10))
                .ReturnsAsync(purchasedIds);

            mockSearchProvider.Setup(s => s.RecommendAsync(
                    It.IsAny<IReadOnlyCollection<ObjectId>>(),
                    It.IsAny<IReadOnlyCollection<ObjectId>>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(recommendedGames);

            var service = new GameService(
                mockGameRepo.Object,
                mockPurchaseRepo.Object,
                mockEventRepo.Object,
                mockSearchProvider.Object,
                mockConfiguration.Object);

            // Act
            var result = await service.GetRecommendationsAsync(userId, 1);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Recomendado", result[0].Name);
            mockSearchProvider.Verify(s => s.RecommendAsync(
                It.IsAny<IReadOnlyCollection<ObjectId>>(),
                It.IsAny<IReadOnlyCollection<ObjectId>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "Deve fazer fallback para populares quando search provider não retorna recomendações")]
        public async Task GetRecommendationsAsync_SearchProviderSemResultados_DeveRetornarPopulares()
        {
            // Arrange
            var mockPurchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);
            var mockGameRepo = new Mock<IGameRepository>(MockBehavior.Strict);
            var mockEventRepo = new Mock<IEventRepository>(MockBehavior.Strict);
            var mockSearchProvider = new Mock<IGameSearchProvider>(MockBehavior.Loose);
            var mockConfiguration = new Mock<IConfiguration>();

            mockEventRepo
                .Setup(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var userId = ObjectId.GenerateNewId();
            var purchasedIds = new List<ObjectId> { ObjectId.GenerateNewId() };
            var popularGames = new List<ProjectGameDTO>
            {
                new ProjectGameDTO { _id = ObjectId.GenerateNewId(), Name = "Popular Fallback", Category = "Ação", Price = 99 }
            };

            mockPurchaseRepo.Setup(r => r.GetUserPaidGameIdsAsync(userId, 10))
                .ReturnsAsync(purchasedIds);
            mockPurchaseRepo.Setup(r => r.GetTopPopularAsync(It.IsAny<int>()))
                .ReturnsAsync(popularGames);

            // Search provider retorna lista vazia
            mockSearchProvider.Setup(s => s.RecommendAsync(
                    It.IsAny<IReadOnlyCollection<ObjectId>>(),
                    It.IsAny<IReadOnlyCollection<ObjectId>>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProjectGameDTO>());

            var service = new GameService(
                mockGameRepo.Object,
                mockPurchaseRepo.Object,
                mockEventRepo.Object,
                mockSearchProvider.Object,
                mockConfiguration.Object);

            // Act
            var result = await service.GetRecommendationsAsync(userId, 1);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Popular Fallback", result[0].Name);
        }

        [Fact(DisplayName = "Deve indexar jogo no search provider ao criar")]
        public async Task CreateAsync_DeveIndexarNoSearchProvider()
        {
            var dto = new CreateGameDTO
            {
                Name = "Test",
                Description = "Description test",
                Category = "FPS",
                ReleaseDate = DateTime.Now.AddMonths(-1),
                LastUpdateDate = DateTime.Now,
                Price = 59.99m
            };

            var response = await _service.CreateAsync(dto);

            Assert.False(response.HasError);
            // Verifica que o search provider foi chamado para indexar
            _mockSearchProvider.Verify(s => s.UpsertAsync(It.IsAny<ProjectGameDTO>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact(DisplayName = "Deve remover do índice ao deletar jogo")]
        public async Task DeleteAsync_DeveRemoverDoIndice()
        {
            var gameId = ObjectId.GenerateNewId();

            await _service!.DeleteAsync(gameId);

            _mockRepo!.Verify(r => r.DeleteAsync(gameId), Times.Once);
            _mockSearchProvider.Verify(s => s.DeleteAsync(gameId.ToString(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
    }
}
