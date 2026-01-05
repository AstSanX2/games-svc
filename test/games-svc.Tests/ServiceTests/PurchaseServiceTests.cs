using Application.Services;
using AutoFixture;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Moq;
using Xunit;

namespace games_svc.Tests.ServiceTests
{
    public class PurchaseServiceTests
    {
        private readonly Mock<IPurchaseRepository> _mockPurchaseRepo;
        private readonly Mock<IEventRepository> _mockEventRepo;
        private readonly Mock<IOutboxRepository> _mockOutboxRepo;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly IPurchaseService _service;
        private readonly List<Purchase> _stubPurchases;

        public PurchaseServiceTests()
        {
            _mockPurchaseRepo = new Mock<IPurchaseRepository>(MockBehavior.Strict);
            _mockEventRepo = new Mock<IEventRepository>(MockBehavior.Strict);
            _mockOutboxRepo = new Mock<IOutboxRepository>(MockBehavior.Strict);
            _mockConfiguration = new Mock<IConfiguration>();

            _stubPurchases = new List<Purchase>();

            // Setup configuration (SQS não configurado para testes)
            _mockConfiguration.Setup(c => c["Sqs:ServiceUrl"]).Returns((string?)null);
            _mockConfiguration.Setup(c => c["AWS:AccessKey"]).Returns((string?)null);
            _mockConfiguration.Setup(c => c["AWS:SecretKey"]).Returns((string?)null);
            _mockConfiguration.Setup(c => c["AWS:Region"]).Returns((string?)null);
            _mockConfiguration.Setup(c => c["Sqs:PaymentsQueueUrl"]).Returns((string?)null);
            _mockConfiguration.Setup(c => c["PAYMENTS_QUEUE_URL"]).Returns((string?)null);

            // Setup event repository
            _mockEventRepo
                .Setup(e => e.AppendEventAsync(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _mockOutboxRepo
                .Setup(o => o.EnqueueAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Setup purchase repository
            _mockPurchaseRepo.Setup(r => r.CreateAsync(It.IsAny<Purchase>(), It.IsAny<CancellationToken>()))
                .Callback<Purchase, CancellationToken>((p, ct) =>
                {
                    p._id = ObjectId.GenerateNewId();
                    _stubPurchases.Add(p);
                })
                .Returns(Task.CompletedTask);

            _service = new PurchaseService(
                _mockPurchaseRepo.Object,
                _mockEventRepo.Object,
                _mockOutboxRepo.Object,
                _mockConfiguration.Object);
        }

        [Fact(DisplayName = "CreateAsync deve criar compra com status PENDING")]
        public async Task CreateAsync_CreatesWithPendingStatus()
        {
            // Arrange
            var gameId = ObjectId.GenerateNewId();
            var userId = ObjectId.GenerateNewId();
            var amount = 59.90m;

            // Act
            var result = await _service.CreateAsync(gameId, amount, userId, CancellationToken.None);

            // Assert
            Assert.NotEqual(ObjectId.Empty, result);
            Assert.Single(_stubPurchases);
            Assert.Equal("PENDING", _stubPurchases[0].Status);
        }

        [Fact(DisplayName = "CreateAsync deve registrar evento GamePurchased")]
        public async Task CreateAsync_PublishesGamePurchasedEvent()
        {
            // Arrange
            var gameId = ObjectId.GenerateNewId();
            var userId = ObjectId.GenerateNewId();
            var amount = 29.90m;

            // Act
            await _service.CreateAsync(gameId, amount, userId, CancellationToken.None);

            // Assert
            _mockEventRepo.Verify(e =>
                e.AppendEventAsync(It.Is<DomainEvent>(ev => ev.Type == "GamePurchased"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact(DisplayName = "CreateAsync deve chamar repositório com dados corretos")]
        public async Task CreateAsync_CallsRepositoryWithCorrectData()
        {
            // Arrange
            var gameId = ObjectId.GenerateNewId();
            var userId = ObjectId.GenerateNewId();
            var amount = 99.90m;

            // Act
            await _service.CreateAsync(gameId, amount, userId, CancellationToken.None);

            // Assert
            _mockPurchaseRepo.Verify(r =>
                r.CreateAsync(It.Is<Purchase>(p =>
                    p.GameId == gameId &&
                    p.UserId == userId &&
                    p.Amount == amount &&
                    p.Status == "PENDING"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact(DisplayName = "CreateAsync deve retornar o ID da compra criada")]
        public async Task CreateAsync_ReturnsCreatedPurchaseId()
        {
            // Arrange
            var gameId = ObjectId.GenerateNewId();
            var userId = ObjectId.GenerateNewId();
            var amount = 49.90m;

            // Act
            var result = await _service.CreateAsync(gameId, amount, userId, CancellationToken.None);

            // Assert
            Assert.NotEqual(ObjectId.Empty, result);
            Assert.Equal(_stubPurchases[0]._id, result);
        }

        [Fact(DisplayName = "CreateAsync deve definir CreatedAt como data/hora atual")]
        public async Task CreateAsync_SetsCreatedAtToNow()
        {
            // Arrange
            var gameId = ObjectId.GenerateNewId();
            var userId = ObjectId.GenerateNewId();
            var amount = 19.90m;
            var beforeCreate = DateTime.UtcNow;

            // Act
            await _service.CreateAsync(gameId, amount, userId, CancellationToken.None);

            // Assert
            var afterCreate = DateTime.UtcNow;
            Assert.InRange(_stubPurchases[0].CreatedAt, beforeCreate, afterCreate);
        }

        [Fact(DisplayName = "CreateAsync deve funcionar com amount zero")]
        public async Task CreateAsync_WorksWithZeroAmount()
        {
            // Arrange
            var gameId = ObjectId.GenerateNewId();
            var userId = ObjectId.GenerateNewId();
            var amount = 0m;

            // Act
            var result = await _service.CreateAsync(gameId, amount, userId, CancellationToken.None);

            // Assert
            Assert.NotEqual(ObjectId.Empty, result);
            Assert.Equal(0m, _stubPurchases[0].Amount);
        }
    }
}

