Dir[File.join(__dir__, "test_*.rb")].sort.each { |f| require f }
