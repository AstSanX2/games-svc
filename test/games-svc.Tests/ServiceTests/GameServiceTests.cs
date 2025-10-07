using Application.DTO.GameDTO;
using Application.Services;
using AutoFixture;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using MongoDB.Bson;
using Moq;

namespace games_svc.Tests.ServiceTests
{
    public class GameServiceTests : BaseTests
    {
        private List<Game> _stubList;
        private Mock<IGameRepository> _mockRepo;
        private IGameService _service;

        public GameServiceTests()
        {
        }

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
            var mockPurchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);

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

            _mockRepo.Setup(r => r.SearchAtlasAsync(It.IsAny<SearchGameDTO>()))
                .ReturnsAsync(new List<ProjectGameSearchDTO>
                {
                    new ProjectGameSearchDTO { Id = ObjectId.GenerateNewId().ToString(), Name = "Game1", Category = "A", Price = 10, Score = 0.9 }
                });

            _mockRepo.Setup(r => r.RecommendBySimilarAsync(
                    It.IsAny<IReadOnlyCollection<ObjectId>>(),
                    It.IsAny<IReadOnlyCollection<ObjectId>>(),
                    It.IsAny<int>()))
                .ReturnsAsync((IReadOnlyCollection<ObjectId> like, IReadOnlyCollection<ObjectId> exclude, int limit) =>
                    _stubList!.Where(x => !exclude.Contains(x._id)).Take(limit).Select(x => new ProjectGameDTO(x)).ToList());

            // IPurchaseRepository setups
            mockPurchaseRepo.Setup(r => r.GetTopPopularAsync(It.IsAny<int>()))
                .ReturnsAsync((int limit) =>
                    _stubList!.Take(limit).Select(x => new ProjectGameDTO(x)).ToList());

            mockPurchaseRepo.Setup(r => r.GetUserPaidGameIdsAsync(It.IsAny<ObjectId>(), It.IsAny<int>()))
                .ReturnsAsync((ObjectId userId, int max) => new List<ObjectId>());

            // Instancia o serviço com ambos os repositórios
            _service = new GameService(_mockRepo.Object, mockPurchaseRepo.Object);
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
        }

        [Fact(DisplayName = "Deve atualizar um jogo chamando o repositório")]
        public async Task UpdateAsync_CallsRepository()
        {
            var updateDto = _fixture.Build<UpdateGameDTO>().Create();

            await _service!.UpdateAsync(ObjectId.Empty, updateDto);

            _mockRepo!.Verify(r => r.UpdateAsync(ObjectId.Empty, updateDto), Times.Once);
        }

        [Fact(DisplayName = "Deve remover um jogo chamando o repositório")]
        public async Task DeleteAsync_CallsRepository()
        {
            await _service!.DeleteAsync(ObjectId.Empty);

            _mockRepo!.Verify(r => r.DeleteAsync(ObjectId.Empty), Times.Once);
        }

        [Fact(DisplayName = "Deve buscar jogos com SearchAsync")]
        public async Task SearchAsync_DeveRetornarResultados()
        {
            // Arrange
            var mockPurchaseRepo = new Mock<IPurchaseRepository>();
            var mockGameRepo = new Mock<IGameRepository>();
            var searchResult = new List<ProjectGameSearchDTO>
            {
                new ProjectGameSearchDTO { Id = "1", Name = "Jogo 1", Category = "Ação", Price = 10, Score = 0.9 }
            };
            mockGameRepo.Setup(r => r.SearchAtlasAsync(It.IsAny<SearchGameDTO>()))
                .ReturnsAsync(searchResult);

            var service = new GameService(mockGameRepo.Object, mockPurchaseRepo.Object);

            // Act
            var result = await service.SearchAsync(new SearchGameDTO { Q = "Jogo" });

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Jogo 1", result[0].Name);
        }

        [Fact(DisplayName = "Deve retornar jogos populares com GetPopularAsync")]
        public async Task GetPopularAsync_DeveRetornarPopulares()
        {
            // Arrange
            var mockPurchaseRepo = new Mock<IPurchaseRepository>();
            var mockGameRepo = new Mock<IGameRepository>();
            var popularGames = new List<ProjectGameDTO>
            {
                new ProjectGameDTO { _id = ObjectId.GenerateNewId(), Name = "Popular", Category = "Ação", Price = 99 }
            };
            mockPurchaseRepo.Setup(r => r.GetTopPopularAsync(It.IsAny<int>()))
                .ReturnsAsync(popularGames);

            var service = new GameService(mockGameRepo.Object, mockPurchaseRepo.Object);

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
            var mockPurchaseRepo = new Mock<IPurchaseRepository>();
            var mockGameRepo = new Mock<IGameRepository>();
            var userId = ObjectId.GenerateNewId();
            var popularGames = new List<ProjectGameDTO>
            {
                new ProjectGameDTO { _id = ObjectId.GenerateNewId(), Name = "Popular", Category = "Ação", Price = 99 }
            };
            mockPurchaseRepo.Setup(r => r.GetUserPaidGameIdsAsync(userId, 10))
                .ReturnsAsync(new List<ObjectId>());
            mockPurchaseRepo.Setup(r => r.GetTopPopularAsync(It.IsAny<int>()))
                .ReturnsAsync(popularGames);

            var service = new GameService(mockGameRepo.Object, mockPurchaseRepo.Object);

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
            var mockPurchaseRepo = new Mock<IPurchaseRepository>();
            var mockGameRepo = new Mock<IGameRepository>();
            var userId = ObjectId.GenerateNewId();
            var purchasedIds = new List<ObjectId> { ObjectId.GenerateNewId() };
            var recommendedGames = new List<ProjectGameDTO>
            {
                new ProjectGameDTO { _id = ObjectId.GenerateNewId(), Name = "Recomendado", Category = "Ação", Price = 99 }
            };
            mockPurchaseRepo.Setup(r => r.GetUserPaidGameIdsAsync(userId, 10))
                .ReturnsAsync(purchasedIds);
            mockGameRepo.Setup(r => r.RecommendBySimilarAsync(purchasedIds, purchasedIds, 1))
                .ReturnsAsync(recommendedGames);

            var service = new GameService(mockGameRepo.Object, mockPurchaseRepo.Object);

            // Act
            var result = await service.GetRecommendationsAsync(userId, 1);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Recomendado", result[0].Name);
        }
    }
}
