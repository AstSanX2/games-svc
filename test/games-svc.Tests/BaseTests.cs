using AutoFixture;

namespace games_svc.Tests
{
    public abstract class BaseTests
    {
        protected readonly Fixture _fixture;


        public BaseTests()
        {
            _fixture = new();

            InitStubs();
            MockDependencies();
        }

        protected abstract void InitStubs();

        protected abstract void MockDependencies();
    }
}